using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using RitualWorks.Contracts;
using RitualWorks.DTOs;
using RitualWorks.Db;
using RitualWorks.Settings;

namespace RitualWorks.Services
{
    public class RitualService : IRitualService
    {
        private readonly IRitualRepository _ritualRepository;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName;

        public RitualService(IRitualRepository ritualRepository, BlobServiceClient blobServiceClient, IOptions<BlobSettings> blobSettings)
        {
            _ritualRepository = ritualRepository;
            _blobServiceClient = blobServiceClient;
            _containerName = blobSettings.Value.ContainerName ?? throw new ArgumentNullException(nameof(blobSettings), "Container name not configured");
        }

        public async Task<RitualDto> CreateRitualAsync(CreateRitualDto ritualDto, Stream? mediaStream = null)
        {
            string? mediaUrl = null;
            if (mediaStream != null)
            {
                mediaUrl = await UploadFileToBlobAsync(mediaStream, ritualDto.Title + "-media");
            }

            var ritual = new Ritual
            {
                Title = ritualDto.Title,
                Description = ritualDto.Description,
                Preview = ritualDto.Preview,
                FullContent = ritualDto.FullContent,
                ExternalLink = ritualDto.ExternalLink,
                TokenAmount = ritualDto.TokenAmount,
                RitualType = ritualDto.RitualType,
                MediaUrl = mediaUrl
            };

            var createdRitual = await _ritualRepository.CreateRitualAsync(ritual);

            return new RitualDto
            {
                Id = createdRitual.Id,
                Title = createdRitual.Title,
                Description = createdRitual.Description,
                Preview = createdRitual.Preview,
                FullContent = createdRitual.FullContent,
                ExternalLink = createdRitual.ExternalLink,
                TokenAmount = createdRitual.TokenAmount,
                RitualType = createdRitual.RitualType,
                IsLocked = createdRitual.IsLocked,
                Rating = createdRitual.Rating
            };
        }

        public async Task<RitualDto?> UpdateRitualAsync(int id, CreateRitualDto ritualDto, Stream? mediaStream = null)
        {
            var ritual = await _ritualRepository.GetRitualByIdAsync(id);
            if (ritual == null || ritual.IsLocked)
            {
                return null;
            }

            if (mediaStream != null)
            {
                ritual.MediaUrl = await UploadFileToBlobAsync(mediaStream, ritualDto.Title + "-media");
            }

            ritual.Title = ritualDto.Title;
            ritual.Description = ritualDto.Description;
            ritual.Preview = ritualDto.Preview;
            ritual.FullContent = ritualDto.FullContent;
            ritual.ExternalLink = ritualDto.ExternalLink;
            ritual.TokenAmount = ritualDto.TokenAmount;
            ritual.RitualType = ritualDto.RitualType;

            var updatedRitual = await _ritualRepository.UpdateRitualAsync(ritual);

            if (updatedRitual == null)
            {
                return null;
            }

            return new RitualDto
            {
                Id = updatedRitual.Id,
                Title = updatedRitual.Title,
                Description = updatedRitual.Description,
                Preview = updatedRitual.Preview,
                FullContent = updatedRitual.FullContent,
                ExternalLink = updatedRitual.ExternalLink,
                TokenAmount = updatedRitual.TokenAmount,
                RitualType = updatedRitual.RitualType,
                IsLocked = updatedRitual.IsLocked,
                Rating = updatedRitual.Rating
            };
        }

        public async Task<RitualDto?> GetRitualByIdAsync(int id)
        {
            var ritual = await _ritualRepository.GetRitualByIdAsync(id);
            if (ritual == null)
            {
                return null;
            }
            return new RitualDto
            {
                Id = ritual.Id,
                Title = ritual.Title,
                Description = ritual.Description,
                Preview = ritual.Preview,
                FullContent = ritual.FullContent,
                ExternalLink = ritual.ExternalLink,
                TokenAmount = ritual.TokenAmount,
                RitualType = ritual.RitualType,
                IsLocked = ritual.IsLocked,
                Rating = ritual.Rating
            };
        }

        public async Task<IEnumerable<RitualDto>> GetAllRitualsAsync()
        {
            var rituals = await _ritualRepository.GetAllRitualsAsync();
            return rituals.Select(r => new RitualDto
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                Preview = r.Preview,
                FullContent = r.FullContent,
                ExternalLink = r.ExternalLink,
                TokenAmount = r.TokenAmount,
                RitualType = r.RitualType,
                IsLocked = r.IsLocked,
                Rating = r.Rating
            });
        }

        public async Task<bool> LockRitualAsync(int id)
        {
            return await _ritualRepository.LockRitualAsync(id);
        }

        public async Task<bool> RateRitualAsync(int id, double rating)
        {
            return await _ritualRepository.RateRitualAsync(id, rating);
        }

        public async Task<IEnumerable<RitualDto>> SearchRitualsAsync(string query, RitualTypeEnum? type)
        {
            var rituals = await _ritualRepository.SearchRitualsAsync(query, type);
            return rituals.Select(r => new RitualDto
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                Preview = r.Preview,
                FullContent = r.FullContent,
                ExternalLink = r.ExternalLink,
                TokenAmount = r.TokenAmount,
                RitualType = r.RitualType,
                IsLocked = r.IsLocked,
                Rating = r.Rating
            });
        }

        private async Task<string> UploadFileToBlobAsync(Stream fileStream, string fileName)
        {
            var blobContainerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = blobContainerClient.GetBlobClient(fileName);

            await blobClient.UploadAsync(fileStream, overwrite: true);
            return blobClient.Uri.ToString();
        }
    }
}
