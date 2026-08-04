using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subconscious.Engine.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    tag = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "networks",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    uuid = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    default_workspace_uuid = table.Column<string>(type: "TEXT", nullable: true),
                    passphrase = table.Column<byte[]>(type: "BLOB", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_networks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "skill_registry",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    uuid = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    alias = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    source_type = table.Column<string>(type: "TEXT", nullable: false),
                    install_path = table.Column<string>(type: "TEXT", nullable: true),
                    version = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    required_tools = table.Column<string>(type: "TEXT", nullable: true),
                    metadata_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_registry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tool_registry",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    uuid = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    alias = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    tool_type = table.Column<string>(type: "TEXT", nullable: false),
                    script_path = table.Column<string>(type: "TEXT", nullable: true),
                    script_language = table.Column<string>(type: "TEXT", nullable: true),
                    endpoint_url = table.Column<string>(type: "TEXT", nullable: true),
                    auth_type = table.Column<string>(type: "TEXT", nullable: true),
                    auth_env_var = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_registry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    network_id = table.Column<int>(type: "INTEGER", nullable: false),
                    uuid = table.Column<string>(type: "TEXT", nullable: false),
                    tools_config = table.Column<string>(type: "TEXT", nullable: true),
                    skills_config = table.Column<string>(type: "TEXT", nullable: true),
                    directories = table.Column<string>(type: "TEXT", nullable: true),
                    approval_config = table.Column<string>(type: "TEXT", nullable: true),
                    rag_config = table.Column<string>(type: "TEXT", nullable: true),
                    default_model_id = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspaces", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspaces_networks_network_id",
                        column: x => x.network_id,
                        principalTable: "networks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    workspace_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    phone = table.Column<string>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_contacts_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexed_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    workspace_id = table.Column<int>(type: "INTEGER", nullable: false),
                    path = table.Column<string>(type: "TEXT", nullable: false),
                    directory = table.Column<string>(type: "TEXT", nullable: true),
                    size = table.Column<int>(type: "INTEGER", nullable: true),
                    mtime = table.Column<int>(type: "INTEGER", nullable: true),
                    content_hash = table.Column<string>(type: "TEXT", nullable: true),
                    chunk_count = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    indexed_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexed_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_indexed_documents_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notes",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    workspace_id = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    tags = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notes", x => x.id);
                    table.ForeignKey(
                        name: "FK_notes_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "threads",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    uuid = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    default_model_id = table.Column<string>(type: "TEXT", nullable: true),
                    tools_config = table.Column<string>(type: "TEXT", nullable: true),
                    skills_config = table.Column<string>(type: "TEXT", nullable: true),
                    approval_config = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_threads", x => x.id);
                    table.ForeignKey(
                        name: "FK_threads_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    document_id = table.Column<int>(type: "INTEGER", nullable: false),
                    workspace_id = table.Column<int>(type: "INTEGER", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    start_line = table.Column<int>(type: "INTEGER", nullable: true),
                    end_line = table.Column<int>(type: "INTEGER", nullable: true),
                    token_estimate = table.Column<int>(type: "INTEGER", nullable: true),
                    embedding = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_chunks_indexed_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "indexed_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_chunks_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    uuid = table.Column<string>(type: "TEXT", nullable: false),
                    thread_id = table.Column<int>(type: "INTEGER", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_threads_thread_id",
                        column: x => x.thread_id,
                        principalTable: "threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "todo_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    workspace_id = table.Column<int>(type: "INTEGER", nullable: false),
                    thread_id = table.Column<int>(type: "INTEGER", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    priority = table.Column<string>(type: "TEXT", nullable: false),
                    due_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_todo_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_todo_items_threads_thread_id",
                        column: x => x.thread_id,
                        principalTable: "threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_todo_items_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_memory",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    workspace_id = table.Column<int>(type: "INTEGER", nullable: false),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    source_thread_id = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_memory", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_memory_threads_source_thread_id",
                        column: x => x.source_thread_id,
                        principalTable: "threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_workspace_memory_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_app_state_key_tag",
                table: "app_state",
                columns: new[] { "key", "tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contacts_workspace_id",
                table: "contacts",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_document_id",
                table: "document_chunks",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_workspace_id",
                table: "document_chunks",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_indexed_documents_path",
                table: "indexed_documents",
                column: "path");

            migrationBuilder.CreateIndex(
                name: "IX_indexed_documents_workspace_id",
                table: "indexed_documents",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_thread_id",
                table: "messages",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_uuid",
                table: "messages",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_networks_uuid",
                table: "networks",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notes_workspace_id",
                table: "notes",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_skill_registry_uuid",
                table: "skill_registry",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_threads_uuid",
                table: "threads",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_threads_workspace_id",
                table: "threads",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_todo_items_thread_id",
                table: "todo_items",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_todo_items_workspace_id",
                table: "todo_items",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_tool_registry_uuid",
                table: "tool_registry",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_memory_source_thread_id",
                table: "workspace_memory",
                column: "source_thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_memory_workspace_id",
                table: "workspace_memory",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_network_id",
                table: "workspaces",
                column: "network_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_state");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "notes");

            migrationBuilder.DropTable(
                name: "skill_registry");

            migrationBuilder.DropTable(
                name: "todo_items");

            migrationBuilder.DropTable(
                name: "tool_registry");

            migrationBuilder.DropTable(
                name: "workspace_memory");

            migrationBuilder.DropTable(
                name: "indexed_documents");

            migrationBuilder.DropTable(
                name: "threads");

            migrationBuilder.DropTable(
                name: "workspaces");

            migrationBuilder.DropTable(
                name: "networks");
        }
    }
}
