using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Data.Entities;

namespace Subconscious.Engine.Data;

/// <summary>
/// EF Core DbContext for the Subconscious engine database.
/// Compatible with existing Python SQLAlchemy schema - same table and column names.
/// </summary>
public class SubconsciousDbContext : DbContext
{
    public SubconsciousDbContext(DbContextOptions<SubconsciousDbContext> options)
        : base(options)
    {
    }

    // Core entities
    public DbSet<Network> Networks { get; set; } = null!;
    public DbSet<Workspace> Workspaces { get; set; } = null!;
    public DbSet<Entities.Thread> Threads { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<AppState> AppState { get; set; } = null!;

    // Workspace-scoped data
    public DbSet<TodoItem> TodoItems { get; set; } = null!;
    public DbSet<WorkspaceMemory> WorkspaceMemories { get; set; } = null!;
    public DbSet<Note> Notes { get; set; } = null!;
    public DbSet<Contact> Contacts { get; set; } = null!;

    // Skills and Tools registry
    public DbSet<SkillRegistry> SkillRegistry { get; set; } = null!;
    public DbSet<ToolRegistry> ToolRegistry { get; set; } = null!;

    // RAG / Indexing
    public DbSet<IndexedDocument> IndexedDocuments { get; set; } = null!;
    public DbSet<DocumentChunk> DocumentChunks { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Network
        modelBuilder.Entity<Network>(entity =>
        {
            entity.ToTable("networks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Uuid).HasColumnName("uuid").IsRequired();
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DefaultWorkspaceUuid).HasColumnName("default_workspace_uuid");
            entity.Property(e => e.Passphrase).HasColumnName("passphrase");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        // Workspace
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.NetworkId).HasColumnName("network_id").IsRequired();
            entity.Property(e => e.Uuid).HasColumnName("uuid").IsRequired();
            entity.Property(e => e.ToolsConfig).HasColumnName("tools_config");
            entity.Property(e => e.SkillsConfig).HasColumnName("skills_config");
            entity.Property(e => e.Directories).HasColumnName("directories");
            entity.Property(e => e.ApprovalConfig).HasColumnName("approval_config");
            entity.Property(e => e.RagConfig).HasColumnName("rag_config");
            entity.Property(e => e.DefaultModelId).HasColumnName("default_model_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(w => w.Network)
                .WithMany(n => n.Workspaces)
                .HasForeignKey(w => w.NetworkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Thread
        modelBuilder.Entity<Entities.Thread>(entity =>
        {
            entity.ToTable("threads");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Uuid).HasColumnName("uuid").IsRequired();
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DefaultModelId).HasColumnName("default_model_id");
            entity.Property(e => e.ToolsConfig).HasColumnName("tools_config");
            entity.Property(e => e.SkillsConfig).HasColumnName("skills_config");
            entity.Property(e => e.ApprovalConfig).HasColumnName("approval_config");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(t => t.Workspace)
                .WithMany(w => w.Threads)
                .HasForeignKey(t => t.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Message
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Uuid).HasColumnName("uuid").IsRequired();
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.Property(e => e.ThreadId).HasColumnName("thread_id").IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(m => m.Thread)
                .WithMany(t => t.Messages)
                .HasForeignKey(m => m.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AppState
        modelBuilder.Entity<AppState>(entity =>
        {
            entity.ToTable("app_state");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key").IsRequired();
            entity.Property(e => e.Value).HasColumnName("value").IsRequired();
            entity.Property(e => e.Tag).HasColumnName("tag");
            entity.Property(e => e.Client).HasColumnName("client");

            // Client-scoped upserts keep independent UI clients on the same engine isolated.
            entity.HasIndex(e => new { e.Key, e.Tag, e.Client })
                .IsUnique()
                .HasDatabaseName("uq_app_state_key_tag_client");
        });

        // TodoItem
        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.ToTable("todo_items");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.ThreadId).HasColumnName("thread_id");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Priority).HasColumnName("priority").IsRequired();
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(t => t.Workspace)
                .WithMany(w => w.TodoItems)
                .HasForeignKey(t => t.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.Thread)
                .WithMany(th => th.TodoItems)
                .HasForeignKey(t => t.ThreadId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // WorkspaceMemory
        modelBuilder.Entity<WorkspaceMemory>(entity =>
        {
            entity.ToTable("workspace_memory");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.Key).HasColumnName("key").IsRequired();
            entity.Property(e => e.Value).HasColumnName("value").IsRequired();
            entity.Property(e => e.SourceThreadId).HasColumnName("source_thread_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(m => m.Workspace)
                .WithMany(w => w.Memories)
                .HasForeignKey(m => m.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.SourceThread)
                .WithMany(t => t.Memories)
                .HasForeignKey(m => m.SourceThreadId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Note
        modelBuilder.Entity<Note>(entity =>
        {
            entity.ToTable("notes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.Tags).HasColumnName("tags");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(n => n.Workspace)
                .WithMany(w => w.Notes)
                .HasForeignKey(n => n.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Contact
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("contacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(c => c.Workspace)
                .WithMany(w => w.Contacts)
                .HasForeignKey(c => c.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SkillRegistry
        modelBuilder.Entity<SkillRegistry>(entity =>
        {
            entity.ToTable("skill_registry");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Uuid).HasColumnName("uuid").IsRequired();
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Alias).HasColumnName("alias");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Source).HasColumnName("source").IsRequired();
            entity.Property(e => e.SourceType).HasColumnName("source_type").IsRequired();
            entity.Property(e => e.InstallPath).HasColumnName("install_path");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.RequiredTools).HasColumnName("required_tools");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        // ToolRegistry
        modelBuilder.Entity<ToolRegistry>(entity =>
        {
            entity.ToTable("tool_registry");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Uuid).HasColumnName("uuid").IsRequired();
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Alias).HasColumnName("alias");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ToolType).HasColumnName("tool_type").IsRequired();
            entity.Property(e => e.ScriptPath).HasColumnName("script_path");
            entity.Property(e => e.ScriptLanguage).HasColumnName("script_language");
            entity.Property(e => e.EndpointUrl).HasColumnName("endpoint_url");
            entity.Property(e => e.AuthType).HasColumnName("auth_type");
            entity.Property(e => e.AuthEnvVar).HasColumnName("auth_env_var");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        // IndexedDocument
        modelBuilder.Entity<IndexedDocument>(entity =>
        {
            entity.ToTable("indexed_documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.HasIndex(e => e.WorkspaceId);
            entity.Property(e => e.Path).HasColumnName("path").IsRequired();
            entity.HasIndex(e => e.Path);
            entity.Property(e => e.Directory).HasColumnName("directory");
            entity.Property(e => e.Size).HasColumnName("size");
            entity.Property(e => e.Mtime).HasColumnName("mtime");
            entity.Property(e => e.ContentHash).HasColumnName("content_hash");
            entity.Property(e => e.ChunkCount).HasColumnName("chunk_count").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.IndexedAt).HasColumnName("indexed_at");

            entity.HasOne(d => d.Workspace)
                .WithMany(w => w.IndexedDocuments)
                .HasForeignKey(d => d.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DocumentChunk
        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.ToTable("document_chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id").IsRequired();
            entity.HasIndex(e => e.DocumentId);
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.HasIndex(e => e.WorkspaceId);
            entity.Property(e => e.Ordinal).HasColumnName("ordinal").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.StartLine).HasColumnName("start_line");
            entity.Property(e => e.EndLine).HasColumnName("end_line");
            entity.Property(e => e.TokenEstimate).HasColumnName("token_estimate");
            entity.Property(e => e.Embedding).HasColumnName("embedding");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(c => c.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Workspace)
                .WithMany()
                .HasForeignKey(c => c.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
