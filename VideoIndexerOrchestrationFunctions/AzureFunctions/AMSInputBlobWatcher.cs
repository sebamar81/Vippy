﻿using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Linq;
using System;

namespace OrchestrationFunctions
{
    public static class AMSInputBlobWatcher
    {

        // NOTE: You have to update the WebHookEndpoint and Signing Key that you wish to use in the AppSettings to match
        //       your deployed Notification_Webhook_Function. After deployment, you will have a unique endpoint. 
        static string _webHookEndpoint = Environment.GetEnvironmentVariable("WebHookEndpoint");
        static string _signingKey = Environment.GetEnvironmentVariable("SigningKey");

        [FunctionName("AMSInputBlobWatcher")]        
        public static void Run([BlobTrigger("encoding-input/{name}", Connection = "asdf")]CloudBlockBlob inputBlob, string fileName, TraceWriter log)
        {
            //log.Info($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {inputBlob.Length} Bytes");


            CloudMediaContext context = MediaServicesHelper.Context;

            IAsset newAsset = CopyBlobHelper.CreateAssetFromBlob(inputBlob, fileName, log).GetAwaiter().GetResult();


            // delete the source input from the watch folder
            inputBlob.DeleteIfExists();

            // copy blob into new asset
            // create the encoding job
            IJob job = context.Jobs.Create("MES encode from input container - ABR streaming");
            
            // Get a media processor reference, and pass to it the name of the 
            // processor to use for the specific task.
            IMediaProcessor processor = MediaServicesHelper.GetLatestMediaProcessorByName("Media Encoder Standard");

            ITask task = job.Tasks.AddNew("ABR encoding",
                processor,
                "Adaptive Streaming",
                TaskOptions.None
                );

            task.Priority = 100;

            task.InputAssets.Add(newAsset);

            // setup webhook notification
            byte[] keyBytes = Convert.FromBase64String(_signingKey);

            // Check for existing Notification Endpoint with the name "FunctionWebHook"
            var existingEndpoint = context.NotificationEndPoints.Where(e => e.Name == "FunctionWebHook").FirstOrDefault();
            INotificationEndPoint endpoint = null;

            if (existingEndpoint != null)
            {
                log.Info("webhook endpoint already exists");
                endpoint = (INotificationEndPoint)existingEndpoint;
            }
            else
            {
                endpoint = context.NotificationEndPoints.Create("FunctionWebHook",
                        NotificationEndPointType.WebHook, _webHookEndpoint, keyBytes);
                log.Info($"Notification Endpoint Created with Key : {keyBytes.ToString()}");
            }

            // Add an output asset to contain the results of the job. 
            // This output is specified as AssetCreationOptions.None, which 
            // means the output asset is not encrypted. 
            task.OutputAssets.AddNew(fileName, AssetCreationOptions.None);

            job.Submit();
            Globals.LogMessage(log, $"AMS encoding job submitted for {fileName}");

        }
    }
}