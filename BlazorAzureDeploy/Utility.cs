﻿using ConsoleAppTools;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.Shared.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace BlazorAzureDeploy
{
    static class Utility
    {
        public static void Process(string SourceDir,
                                       CloudBlobContainer container,
                                       IEnumerable<string> extensions,
                                       int cacheControlMaxAgeSeconds,
                                       string defaultContenType,
                                       bool clearContainer)
        {

          

            DirectoryInfo dirInfo = new DirectoryInfo(SourceDir);

            //step 1
            DeleteUnnecessaryFilesFromSourceDir(dirInfo);

            IEnumerable<FileInfo> fileslist = dirInfo.GetFiles("*", SearchOption.AllDirectories);

            //step 2
            ReportFilesWithNoExtension(fileslist);

            //step 3
            EnsureContentTypes(fileslist, defaultContenType);

            //step 4
            if (clearContainer)
            {
                EmptyContainer(container);
            }

            //step 5
            UploadFiles(SourceDir, container, fileslist, extensions, cacheControlMaxAgeSeconds);


        }



        public static void UploadFiles(string SourceDir,
                                 CloudBlobContainer container,
                                 IEnumerable<FileInfo> fileslist,
                                 IEnumerable<string> extensions,
                                 int cacheControlMaxAgeSeconds)
        {

            int SourceDirLenght = SourceDir.Length + 1;


            string cacheControlHeader = "public, max-age=" + cacheControlMaxAgeSeconds.ToString();


            CATFunctions.Print("Processing files...", true, false);

            CATFunctions.ShowProgress(fileslist.Count());
            CATFunctions.StartSubProcess("Uploading files...");


            Parallel.ForEach(fileslist, (fileInfo) =>
            {
                CATFunctions.Progress();
               
                string contentType;

                ContentTypeHelper.CurrentContentTypes.TryGetValue(fileInfo.Extension, out contentType);

                string filePath = GetFilePath(SourceDirLenght, fileInfo);

                CloudBlockBlob blob = container.GetBlockBlobReference(filePath);


                if (extensions.Contains(fileInfo.Extension, StringComparer.OrdinalIgnoreCase))
                {



                    byte[] compressedBytes;


                    CATFunctions.Print("Gzip file - " + filePath);

                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                        using (var blobStream = fileInfo.OpenRead())
                        {
                            blobStream.CopyTo(gzipStream);
                        }

                        compressedBytes = memoryStream.ToArray();

                        CATFunctions.Print("uploading file - " + filePath);
                        blob.UploadFromByteArray(compressedBytes, 0, compressedBytes.Length);

                        blob.Properties.CacheControl = cacheControlHeader;
                        blob.Properties.ContentEncoding = "gzip";
                        blob.Properties.ContentType = contentType;
                        blob.SetProperties();
                    }
                }
                else
                {

                    CATFunctions.Print("uploading file - " + filePath);
                    blob.UploadFromFile(fileInfo.FullName);


                    blob.Properties.CacheControl = cacheControlHeader;
                    blob.Properties.ContentType = contentType;
                    blob.SetProperties();
                }
            });


            CATFunctions.EndSubProcess();
            CATFunctions.FinishProgress();
          
        }

        public static void DeleteUnnecessaryFilesFromSourceDir(DirectoryInfo dirInfo)
        {
            IEnumerable<FileInfo> fileslist = dirInfo.GetFiles("*", SearchOption.AllDirectories).Where(x => x.Extension.Equals(".gz"));

            if (fileslist.Any())
            {
                CATFunctions.Print("======!!!!!!! Warning !!!!!!=======", true, false);
                CATFunctions.Print("Found " + fileslist.Count() + " files with extension .gz");
                CATFunctions.Print(".gz files are not necessary for deploy.", false, true);

                foreach (var item in fileslist)
                {
                    File.Delete(item.FullName);
                    CATFunctions.Print("Deleting - " + item.FullName);
                }
            }
        }

    public static void ReportFilesWithNoExtension(IEnumerable<FileInfo> fileslist)
        {
            if (fileslist.Any(x => string.IsNullOrEmpty(x.Extension)))
            {
               
                CATFunctions.Print("======!!!!!!! Warning !!!!!!=======", true, false);
                CATFunctions.Print("Found " + fileslist.Where(x => string.IsNullOrEmpty(x.Extension)).Count() + " files with no extension", false, true);

                foreach (var item in fileslist.Where(x => string.IsNullOrEmpty(x.Extension)))
                {
                    CATFunctions.Print("Warning - No file extension - " + item.FullName);
                }

             
            }
        }


public static void EmptyContainer(CloudBlobContainer container)
        {

           

            var blobInfos = container.ListBlobs(null, true, BlobListingDetails.None);


            CATFunctions.Print("Deleting old files (" + blobInfos.Count() + ")", true, false);
            CATFunctions.ShowProgress(blobInfos.Count());
            CATFunctions.StartSubProcess("Deleting old files...");



            


            Parallel.ForEach(blobInfos, (blobInfo) =>
            {

                CloudBlob blob = (CloudBlob)blobInfo;
                blob.Delete();

                CATFunctions.Progress();
                CATFunctions.Print("Deleting blob " + blob.Name);
            });


            CATFunctions.Print("Container is empty", true, true);



            CATFunctions.EndSubProcess();
            CATFunctions.FinishProgress();
           
        }


        public static void EnsureContentTypes(IEnumerable<FileInfo> fileslist, string defaultContenType)
        {
            bool b = true;
            
            CATFunctions.Print("Checking content types...", true, true);

            List<string> fileExtensions = fileslist.Select(o => o.Extension).Distinct().ToList();
            ContentTypeHelper.CurrentContentTypes = new Dictionary<string, string>();



            string contentType;
            foreach (var item in fileExtensions)
            {
                if (string.IsNullOrEmpty(item))
                {
                    contentType = defaultContenType;
                }
                else
                {

                    if (ContentTypeHelper.contentTypes.TryGetValue(item, out contentType))
                    {
                        ContentTypeHelper.CurrentContentTypes.Add(item, contentType);
                    }
                    else
                    {
                        CATFunctions.Print("Warning - Content type is missing for " + item);
                        ContentTypeHelper.CurrentContentTypes.Add(item, defaultContenType);
                        b = false;   
                    }                   
                }
            }


            if (!b)
            {
                CATFunctions.Print("======!!!!!!! Warning !!!!!!=======", true, false);
                CATFunctions.Print("For all missing cointent type extensions will be set to default or provided value - " + defaultContenType, false, true);
               
            }



            CATFunctions.Print("Content types are ready.", true, true);
          

        }

        public static string GetFilePath(int SourceDirLenght, FileInfo fi)
        {            
            return fi.FullName.Substring(SourceDirLenght, fi.FullName.Length  - SourceDirLenght);
        }

        public static void SetWildcardCorsOnBlobService(this CloudStorageAccount storageAccount)
        {
            storageAccount.SetCORSPropertiesOnBlobService(cors =>
            {
                var wildcardRule = new CorsRule() { AllowedMethods = CorsHttpMethods.Get, AllowedOrigins = { "*" } };
                cors.CorsRules.Clear();
                cors.CorsRules.Add(wildcardRule);
                return cors;
            });
        }

        public static void SetCORSPropertiesOnBlobService(this CloudStorageAccount storageAccount,
            Func<CorsProperties, CorsProperties> alterCorsRules)
        {
            CATFunctions.Print("Configuring CORS.", true, true);

            if (storageAccount == null || alterCorsRules == null) throw new ArgumentNullException();

            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            ServiceProperties serviceProperties = blobClient.GetServiceProperties();

            serviceProperties.Cors = alterCorsRules(serviceProperties.Cors) ?? new CorsProperties();

            blobClient.SetServiceProperties(serviceProperties);
        }




    }
}
