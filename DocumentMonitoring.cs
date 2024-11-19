using Azure;
using Azure.AI.Translation.Text;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace POC_translate_documents
{
    public class DocumentMonitoring
    {
        private readonly ILogger<DocumentMonitoring> _logger;

        public DocumentMonitoring(ILogger<DocumentMonitoring> logger)
        {
            _logger = logger;
        }

        [Function("Healthcheck")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult($"Welcome to Azure Functions! API endpoints is responding");
        }

        [Function("UploadAndTranslateDocument")]
        public async Task<IActionResult> Translate(
                [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req)
        {
            try
            {
                // Récupération du fichier uploadé
                var file = req.Form.Files[0];
                string originalFileName = file.FileName;

                // Configuration Blob Storage
                string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                    ?? throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set.");
                string containerName = "documents";
                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                
                if(!containerClient.Exists())
                {
                    // Vérifier si le conteneur existe, sinon le créer
                    await containerClient.CreateIfNotExistsAsync();
                }

                // Upload du fichier (écraser si existe)
                var blobClient = containerClient.GetBlobClient(originalFileName);
                using var stream = file.OpenReadStream();
                await blobClient.UploadAsync(stream, overwrite: true);

                // Configuration traduction
                string translatorKey = Environment.GetEnvironmentVariable("TranslatorKey")
                    ?? throw new InvalidOperationException("TranslatorKey environment variable is not set.");
                string translatorEndpoint = Environment.GetEnvironmentVariable("TranslatorEndpoint")
                    ?? throw new InvalidOperationException("TranslatorEndpoint environment variable is not set.");
                var client = new TextTranslationClient(new AzureKeyCredential(translatorKey));

                // Logique de traduction (exemple simplifié)
                var translatedContent = await TranslateDocument(blobClient, client);

                return new OkObjectResult($"Document {originalFileName} traduit avec succès: {translatedContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the document.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        private async Task<string> TranslateDocument(BlobClient originalBlob, TextTranslationClient translationClient)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                await originalBlob.DownloadToAsync(memoryStream);
                memoryStream.Position = 0;

                // Exemple pour un fichier texte simple
                using var reader = new StreamReader(memoryStream);
                string textToTranslate = await reader.ReadToEndAsync();

                // Configuration de la traduction
                string fromLanguage = "en"; // À détecter dynamiquement 
                string[] toLanguages = new[] { "fr" }; // Langues cibles

                var translateResult = await translationClient.TranslateAsync(
                    toLanguages,
                    new[] { textToTranslate },
                    sourceLanguage: fromLanguage
                );

                // Exemple de sauvegarde des traductions
                var translationResults = translateResult.Value
                    .Select(t => new
                    {
                        TargetLanguage = t.DetectedLanguage,
                        TranslatedText = t.SourceText
                    })
                    .ToList();

                return JsonConvert.SerializeObject(translationResults);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("An error occurred during document translation.", ex);
            }
        }
    }
}