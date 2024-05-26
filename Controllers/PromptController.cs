using System.Net;
using Azure;
using System.Text;


using Microsoft.AspNetCore.Mvc;

using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using Microsoft.VisualBasic;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Polly;
using OperationBotWebSvc.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;

using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using Microsoft.AspNetCore.Authorization;
using System;

namespace OperationBotWebSvc.Controllers
{
    [AllowAnonymous]
   
    [ApiController]
    public class PromptController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly TelemetryClient _telemetryClient;
        public PromptController(IConfiguration configuration, TelemetryClient telemetryClient)
        {
            _configuration = configuration;
            _telemetryClient = telemetryClient;
        }

        
        [HttpPost("ProcessPrompt", Name = "ProcessPrompt")]
        public async Task<IActionResult> ProcessPrompt([FromBody] PromtpText prompt)
        {
            return await NewMethod(prompt);
        }

        private async Task<IActionResult> NewMethod(PromtpText prompt)
        {
            string indexName = _configuration["indexName"];
            string indexerName = _configuration["indexerName"];
            string searchServiceName = _configuration["serviceName"];

            try
            {
                string promptText = prompt.input;
                string[] chatHistory = prompt.history;

                string chats = string.Empty;
                if (chatHistory != null && chatHistory.Length > 0)
                {
                    chats = string.Join("", chatHistory);
                }

                _telemetryClient.TrackTrace("ProcessPrompt called with request body prompt : " + promptText + " and chat history: " + chats);
                string apiKey = GetSecret("SearchServiceApiKey").Result;
           
                foreach (var header in Request.Headers)
                {
                    string headerKey = header.Key;
                    string headerValue = header.Value;
                    _telemetryClient.TrackTrace($"Header: {headerKey}, Value: {headerValue}");
                }

                _telemetryClient.TrackTrace("ProcessPrompt-GetSecret done");
                ReadOnlyMemory<float> vectorizedResult = await GetEmbeddings(prompt.input);
                _telemetryClient.TrackTrace("ProcessPrompt-GetEmbeddings done");
                SearchClient searchClient = new SearchClient(new Uri($"https://{searchServiceName}.search.windows.net/"), indexName, new AzureKeyCredential(apiKey));
                _telemetryClient.TrackTrace("ProcessPrompt-SearchClient Satart");
                SearchResults<SearchDocument> response = await searchClient.SearchAsync<SearchDocument>(
                    prompt.input,
                    new SearchOptions
                    {
                        VectorSearch = new()
                        {
                            Queries = { new VectorizedQuery(vectorizedResult) { KNearestNeighborsCount = 3, Fields = { "vector" } } }
                        },
                    });
                _telemetryClient.TrackTrace("ProcessPrompt-SearchClient done");
                List<ContentItem> formattedContents = new List<ContentItem>();
                foreach (SearchResult<SearchDocument> result in response.GetResults())
                {
                    formattedContents.Add(new ContentItem
                    {
                        Content = result.Document["chunk"]?.ToString() ?? string.Empty,
                        ContentLocation = result.Document["ContentLocation"]?.ToString() ?? string.Empty,
                        Title = result.Document["title"]?.ToString() ?? string.Empty
                    });

                }
                _telemetryClient.TrackTrace("ProcessPrompt-SearchClient done");
                string jString = JsonConvert.SerializeObject(formattedContents);
                _telemetryClient.TrackTrace("ProcessPrompt-SerializeObject done");
                //return new OkObjectResult(formattedContents);
                string chatresponse = await GetChatResponse(jString, prompt.input, prompt.history);
                _telemetryClient.TrackTrace("ProcessPrompt-chatresponse done");
                return new OkObjectResult(chatresponse);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
            }
        }

        private async Task<string> GetChatResponse(string searchResult, string promptText, string[] chatHistory )
        {
            string systemPrompt = "You are LA Fitness Operations Manager. Retunn the information from the given context";
            string prompt = promptText;
            Uri endpoint = new Uri(_configuration["openAIEndpoint"]);
            string key = await GetSecret("OpenAIAPIKey");
            OpenAIClient client = new OpenAIClient(endpoint,new AzureKeyCredential(key));   //Environment.GetEnvironmentVariable(

            // Check if chatHistory is null or empty
            string chats = string.Empty;
            if (chatHistory != null && chatHistory.Length > 0)
            {
                chats = string.Join("", chatHistory);
            }

            var chatCompletionsOptions = new ChatCompletionsOptions()
            {
                DeploymentName = "gpt-35-turbo", // Use DeploymentName for "model" with non-Azure clients
                Messages =
                    {
                        // The system message represents instructions or other guidance about how the assistant should behave
                        new ChatRequestSystemMessage("You are a helpful LA fititness operations manager."),
                        // User messages represent current or historical input from the end user
                        new ChatRequestUserMessage(promptText),
                        // Assistant messages represent historical responses from the assistant

                        new ChatRequestAssistantMessage(chats),
       
                    },
                
                Temperature = (float)0.7,
                MaxTokens = 4000,
                NucleusSamplingFactor = (float)0.95,
                FrequencyPenalty = 0,
                PresencePenalty = 0,
            };
            Response<ChatCompletions> response = await client.GetChatCompletionsAsync(chatCompletionsOptions);
            ChatResponseMessage responseMessage = response.Value.Choices[0].Message;
          
            string messageText = responseMessage.Content;
            return messageText;
        }

        private async Task<ReadOnlyMemory<float>> GetEmbeddings(string input)
        {
            Uri endpoint = new Uri(_configuration["openAIEndpoint"]);
            string key = await GetSecret("OpenAIAPIKey");
            AzureKeyCredential credential = new AzureKeyCredential(key);

            OpenAIClient openAIClient = new OpenAIClient(endpoint, credential);
            EmbeddingsOptions embeddingsOptions = new("text-embedding-ada-002", new string[] { input });

            // Define a Polly retry policy
            var retryPolicy = Policy
                .Handle<RequestFailedException>(ex => ex.Status == 429)
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            // Use the policy to execute the operation
            Embeddings embeddings = await retryPolicy.ExecuteAsync(() => Task.FromResult(openAIClient.GetEmbeddings(embeddingsOptions)));


            return embeddings.Data[0].Embedding;
        }
        //Method to get the config and secrets from Azure Key Vault
        private async Task<string> GetSecret(string secretName)
        {
            _telemetryClient.TrackTrace("ProcessPrompt-GetSecret" + secretName);

            var kvuri = _configuration["kvuri"];
            _telemetryClient.TrackTrace("ProcessPrompt-GetSecret" + kvuri);
            var secretClient = new SecretClient(new Uri(kvuri), new DefaultAzureCredential());
            KeyVaultSecret secret = await secretClient.GetSecretAsync(secretName);
            return secret.Value;
        }

    }
}
