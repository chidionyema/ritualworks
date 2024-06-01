using Microsoft.EntityFrameworkCore;
using RitualWorks.Db;
using RitualWorks.DTOs;
using RitualWorks.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class RitualService : IRitualService
{
    private readonly ApplicationDbContext _context;

    public RitualService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<RitualDto>> GetAllRitualsAsync()
    {
        return await _context.Rituals
            .Select(r => new RitualDto
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                TextContent = r.TextContent,
                AudioUrl = r.AudioUrl,
                VideoUrl = r.VideoUrl,
                RitualTypeId = r.RitualTypeId
            })
            .ToListAsync();
    }

    public async Task<RitualDto?> GetRitualByIdAsync(int id)
    {
        var ritual = await _context.Rituals.FindAsync(id);
        if (ritual == null)
        {
            return null;
        }
        return new RitualDto
        {
            Id = ritual.Id,
            Title = ritual.Title,
            Description = ritual.Description,
            TextContent = ritual.TextContent,
            AudioUrl = ritual.AudioUrl,
            VideoUrl = ritual.VideoUrl,
            RitualTypeId = ritual.RitualTypeId
        };
    }

    public async Task<RitualDto> CreateRitualAsync(CreateRitualDto ritualDto)
    {
        var ritual = new Ritual
        {
            Title = ritualDto.Title,
            Description = ritualDto.Description,
            TextContent = ritualDto.TextContent,
            AudioUrl = ritualDto.AudioUrl,
            VideoUrl = ritualDto.VideoUrl,
            RitualTypeId = ritualDto.RitualTypeId
        };

        _context.Rituals.Add(ritual);
        await _context.SaveChangesAsync();

        return new RitualDto
        {
            Id = ritual.Id,
            Title = ritual.Title,
            Description = ritual.Description,
            TextContent = ritual.TextContent,
            AudioUrl = ritual.AudioUrl,
            VideoUrl = ritual.VideoUrl,
            RitualTypeId = ritual.RitualTypeId
        };
    }

    public async Task<RitualDto?> UpdateRitualAsync(int id, CreateRitualDto ritualDto)
    {
        var ritual = await _context.Rituals.FindAsync(id);
        if (ritual == null)
        {
            return null;
        }

        ritual.Title = ritualDto.Title;
        ritual.Description = ritualDto.Description;
        ritual.TextContent = ritualDto.TextContent;
        ritual.AudioUrl = ritualDto.AudioUrl;
        ritual.VideoUrl = ritualDto.VideoUrl;
        ritual.RitualTypeId = ritualDto.RitualTypeId;

        _context.Entry(ritual).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        return new RitualDto
        {
            Id = ritual.Id,
            Title = ritual.Title,
            Description = ritual.Description,
            TextContent = ritual.TextContent,
            AudioUrl = ritual.AudioUrl,
            VideoUrl = ritual.VideoUrl,
            RitualTypeId = ritual.RitualTypeId
        };
    }

    public async Task<bool> DeleteRitualAsync(int id)
    {
        var ritual = await _context.Rituals.FindAsync(id);
        if (ritual == null)
        {
            return false;
        }

        _context.Rituals.Remove(ritual);
        await _context.SaveChangesAsync();

        return true;
    }
}
