using Microsoft.Extensions.AI;
using Subconscious.Engine.Data.Entities;
using System.ComponentModel;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Tool module for managing contacts in the workspace database.
/// </summary>
public class ContactsToolModule : IToolModule
{
    public string Slug => "contacts";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        var tools = new List<AIFunction>();

        // add_contact
        tools.Add(AIFunctionFactory.Create(
            ([Description("Full name (required).")] string name,
             [Description("Email address (optional).")] string email = "",
             [Description("Phone number (optional).")] string phone = "",
             [Description("Freeform notes about this contact (optional).")] string notes = "") =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var contact = new Contact
                {
                    WorkspaceId = workspaceId,
                    Name = name,
                    Email = email ?? "",
                    Phone = phone ?? "",
                    Notes = notes ?? "",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                context.Database!.Contacts.Add(contact);
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    contact = new
                    {
                        id = contact.Id,
                        name = contact.Name,
                        email = contact.Email,
                        phone = contact.Phone,
                        notes = contact.Notes
                    }
                };
            },
            name: "add_contact",
            description: "Save a new contact for the current workspace."));

        // list_contacts
        tools.Add(AIFunctionFactory.Create(
            () =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var contacts = context.Database!.Contacts
                    .Where(c => c.WorkspaceId == workspaceId)
                    .OrderBy(c => c.Name)
                    .Select(c => new
                    {
                        id = c.Id,
                        name = c.Name,
                        email = c.Email,
                        phone = c.Phone,
                        notes = c.Notes
                    })
                    .ToList();

                return (object)new { contacts };
            },
            name: "list_contacts",
            description: "Return all contacts saved for the current workspace."));

        // find_contact
        tools.Add(AIFunctionFactory.Create(
            ([Description("Search string to match against name or email fields.")] string query) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var contacts = context.Database!.Contacts
                    .Where(c => c.WorkspaceId == workspaceId && 
                               (c.Name.Contains(query) || (c.Email != null && c.Email.Contains(query))))
                    .Select(c => new
                    {
                        id = c.Id,
                        name = c.Name,
                        email = c.Email,
                        phone = c.Phone,
                        notes = c.Notes
                    })
                    .ToList();

                return (object)new { contacts };
            },
            name: "find_contact",
            description: "Search for contacts by name or email (case-insensitive substring match)."));

        // update_contact
        tools.Add(AIFunctionFactory.Create(
            ([Description("ID of the contact to update.")] int contact_id,
             [Description("New name (leave blank to keep current).")] string? name = null,
             [Description("New email (leave blank to keep current).")] string? email = null,
             [Description("New phone (leave blank to keep current).")] string? phone = null,
             [Description("New notes (leave blank to keep current).")] string? notes = null) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var contact = context.Database!.Contacts
                    .FirstOrDefault(c => c.Id == contact_id && c.WorkspaceId == workspaceId);

                if (contact == null)
                    return (object)new { status = "error", message = "Contact not found" };

                if (!string.IsNullOrEmpty(name)) contact.Name = name;
                if (email != null) contact.Email = email;
                if (phone != null) contact.Phone = phone;
                if (notes != null) contact.Notes = notes;

                contact.UpdatedAt = DateTime.UtcNow;
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    contact = new
                    {
                        id = contact.Id,
                        name = contact.Name,
                        email = contact.Email,
                        phone = contact.Phone,
                        notes = contact.Notes
                    }
                };
            },
            name: "update_contact",
            description: "Update one or more fields on an existing contact."));

        // delete_contact
        tools.Add(AIFunctionFactory.Create(
            ([Description("ID of the contact to delete.")] int contact_id) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var contact = context.Database!.Contacts
                    .FirstOrDefault(c => c.Id == contact_id && c.WorkspaceId == workspaceId);

                if (contact == null)
                    return (object)new { status = "error", message = "Contact not found" };

                context.Database.Contacts.Remove(contact);
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    message = $"Contact #{contact_id} deleted"
                };
            },
            name: "delete_contact",
            description: "Permanently delete a contact by ID."));

        return tools;
    }
}
