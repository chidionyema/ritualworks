using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace RitualWorks.Tests
    {
        [Collection("Integration Tests")]
        public class UploadControllerTests
        {
            private readonly IntegrationTestFixture _fixture;

            public UploadControllerTests(IntegrationTestFixture fixture)
            {
                _fixture = fixture;
            }

            //[Fact]
            public async Task UploadChunk_ShouldReturnOk()
            {
                var requestContent = new MultipartFormDataContent
            {
                { new StringContent("1"), "chunkNumber" },
                { new StringContent("1"), "totalChunks" },
                { new StringContent("testfile.txt"), "fileName" },
                { new ByteArrayContent(new byte[] { 1, 2, 3 }), "chunk", "testfile.txt" }
            };

                var response = await _fixture.Client.PostAsync("/api/upload/chunk", requestContent);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                Assert.Contains("Chunk uploaded successfully", responseString);
            }

           // [Fact]
            public async Task UploadFile_ShouldReturnOk()
            {
                var requestContent = new MultipartFormDataContent
            {
                { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "testfile.txt" }
            };

                var response = await _fixture.Client.PostAsync("/api/upload/upload", requestContent);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                Assert.Contains("File uploaded successfully", responseString);
            }


       // [Fact]
        public async Task GenerateSasTokenTest()
        {
            // Upload a file
            var requestContent = new MultipartFormDataContent
            {
                { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "testfile.txt" }
            };

            var uploadResponse = await _fixture.Client.PostAsync("/api/upload/upload", requestContent);
            uploadResponse.EnsureSuccessStatusCode();

            // Generate SAS token
            var generateSasResponse = await _fixture.Client.GetAsync("/api/upload/generate-sas-token/testfile.txt");
            generateSasResponse.EnsureSuccessStatusCode();

            var responseData = await generateSasResponse.Content.ReadAsStringAsync();
            Assert.Contains("sasToken", responseData);
        }

        //[Fact]
            public async Task DownloadFile_ShouldReturnFile()
            {
                // Upload the file first
                var requestContent = new MultipartFormDataContent
            {
                { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "testfile.txt" }
            };

                var uploadResponse = await _fixture.Client.PostAsync("/api/upload/upload", requestContent);
                uploadResponse.EnsureSuccessStatusCode();

                // Download the file
                var downloadResponse = await _fixture.Client.GetAsync("/api/upload/download/testfile.txt");
                downloadResponse.EnsureSuccessStatusCode();

                var fileContent = await downloadResponse.Content.ReadAsByteArrayAsync();
                Assert.Equal(new byte[] { 1, 2, 3 }, fileContent);
            }
        }
}

