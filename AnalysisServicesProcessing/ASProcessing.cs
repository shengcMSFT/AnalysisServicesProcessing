using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Azure.Management.DataFactories.Models;
using Microsoft.Azure.Management.DataFactories.Runtime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;


namespace AnalysisServicesProcessing
{
    public class ASProcessing : IDotNetActivity
    {

        public IDictionary<string, string> Execute(
            IEnumerable<LinkedService> linkedServices,
            IEnumerable<Dataset> datasets,
            Activity activity,
            IActivityLogger logger)
        {
            // get extended properties defined in activity JSON definition
           
            DotNetActivity dotNetActivity = (DotNetActivity)activity.TypeProperties;
            string scriptContainer = dotNetActivity.ExtendedProperties["scriptContainer"];
            string scriptPath = dotNetActivity.ExtendedProperties["scriptPath"];

            // log all extended properties            
            IDictionary<string, string> extendedProperties = dotNetActivity.ExtendedProperties;
            logger.Write("Logging extended properties if any...");

            foreach (KeyValuePair<string, string> entry in extendedProperties)
            {
                logger.Write("<key:{0}> <value:{1}>", entry.Key, entry.Value);
            }

            foreach (LinkedService ls in linkedServices)
                logger.Write("linkedService.Name {0}", ls.Name);

            foreach (Dataset ds in datasets)
                logger.Write("dataset.Name {0}", ds.Name);

            //get Azure Storage Linked Service Connection String from output data set. We use this to access the TMSL script for AS processing
            AzureStorageLinkedService outputLinkedService;
            Dataset outputDataset = datasets.Single(dataset => dataset.Name == activity.Outputs.Single().Name);

            AzureBlobDataset outputTypeProperties;
            outputTypeProperties = outputDataset.Properties.TypeProperties as AzureBlobDataset;

            // get the  Azure Storate linked service from linkedServices object            
            outputLinkedService = linkedServices.First(
                linkedService =>
                linkedService.Name ==
                outputDataset.Properties.LinkedServiceName).Properties.TypeProperties
                as AzureStorageLinkedService;

            // get the connection string in the linked service
            string blobconnectionString = outputLinkedService.ConnectionString;
            
            
            // read the blob content for Analysis Services processing TMSL json script from the extended properties passed to activity
            string asCmdStr = ReadBlob(blobconnectionString, scriptContainer, scriptPath);
            // Connection string to connect to Azure AS. User can do security stuffs here as the extended properties in ADF JSON script is general purpose and does not encrypt the string. 
            string asConnStr = @"<AS Connection String>";
            
            try
            {
                using (AdomdConnection asConn = new AdomdConnection(asConnStr))
                {
                    asConn.Open();
                    AdomdCommand asCmd = asConn.CreateCommand();
                    asCmd.CommandText = asCmdStr;
                    asCmd.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                logger.Write(e.ToString());
            }

            return new Dictionary<string, string>();
        }

        public static string ReadBlob(string connectionString, string blobContainer, string blobPath)
        {
            CloudStorageAccount inputStorageAccount = CloudStorageAccount.Parse(connectionString);
            CloudBlobClient inputClient = inputStorageAccount.CreateCloudBlobClient();
            CloudBlobContainer inputContainer = inputClient.GetContainerReference(blobContainer);
            CloudBlockBlob blockBlob = inputContainer.GetBlockBlobReference(blobPath);

            string CmdStr;
            using (var memoryStream = new MemoryStream())
            {
                blockBlob.DownloadToStream(memoryStream);
                memoryStream.Position = 0;
                StreamReader CmdReader = new StreamReader(memoryStream);
                CmdStr = CmdReader.ReadToEnd();
            }
            return CmdStr;
        }
    }
}
