using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;

namespace Subconscious.Engine.Api.Services;

/// <summary>
/// Implementation of message service with EF Core.
/// </summary>
public class MessageService : IMessageService
{
    private readonly SubconsciousDbContext _context;

    public MessageService(SubconsciousDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<MessageDto>> GetMessagesAsync(string threadUuid, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads
            .FirstOrDefaultAsync(t => t.Uuid == threadUuid, cancellationToken);

        if (thread == null)
            return new List<MessageDto>();

        var messages = await _context.Messages
            .Where(m => m.ThreadId == thread.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return messages.Select(m => MapToDto(m, threadUuid)).ToList();
    }

    public async Task<MessageDto?> GetMessageByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var message = await _context.Messages
            .Include(m => m.Thread)
            .FirstOrDefaultAsync(m => m.Uuid == uuid, cancellationToken);

        return message == null ? null : MapToDto(message, message.Thread!.Uuid);
    }

    public async Task<MessageDto> CreateMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads
            .FirstOrDefaultAsync(t => t.Uuid == request.ThreadUuid, cancellationToken);

        if (thread == null)
            throw new InvalidOperationException($"Thread '{request.ThreadUuid}' not found");

        var message = new Message
        {
            Uuid = Guid.NewGuid().ToString(),
            ThreadId = thread.Id,
            Role = request.Role,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);

        // Update thread timestamp
        thread.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(message, request.ThreadUuid);
    }

    private static MessageDto MapToDto(Message message, string threadUuid)
    {
        return new MessageDto
        {
            Uuid = message.Uuid,
            ThreadUuid = threadUuid,
            Role = message.Role,
            Content = message.Content,
            CreatedAt = message.CreatedAt
        };
    }
}
