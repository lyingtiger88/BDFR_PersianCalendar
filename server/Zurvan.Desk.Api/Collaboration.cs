using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

// Starter collaboration backend, reusing the existing Desk API's authenticated sessions.
// Deployment and durable multi-instance broadcasting are NOT enabled by this source change.
public static class CollaborationApi
{
    public static void MapCollaboration(this WebApplication app)
    {
        app.Services.GetRequiredService<CollaborationStore>();
        app.MapHub<CollaborationHub>("/api/v1/collaboration/live");

        var api = app.MapGroup("/api/v1/collaboration");
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var accounts = http.RequestServices.GetRequiredService<DeskAccountStore>();
            if (CurrentUser(http, accounts) is null) return Results.Unauthorized();
            try { return await next(context); }
            catch (ArgumentException e) { return Results.BadRequest(new ApiError(e.Message)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
            catch (InvalidOperationException e) { return Results.Conflict(new ApiError(e.Message)); }
        });

        api.MapGet("/groups", (HttpContext h, DeskAccountStore a, CollaborationStore s) =>
            Results.Ok(s.GroupsFor(CurrentUser(h, a)!.Id)));

        api.MapPost("/groups", async (GroupCreateRequest request, HttpContext h,
            DeskAccountStore a, CollaborationStore s, IHubContext<CollaborationHub> hub) =>
        {
            var user = CurrentUser(h, a)!;
            var result = s.CreateGroup(user.Id, request);
            await Notify(hub, s, result.Change);
            return Results.Created($"/api/v1/collaboration/groups/{result.Value.Id}", result.Value);
        });

        api.MapGet("/groups/{groupId}", (string groupId, HttpContext h, DeskAccountStore a, CollaborationStore s) =>
            Results.Ok(s.GroupDetails(groupId, CurrentUser(h, a)!.Id)));

        api.MapPost("/groups/{groupId}/invites", async (string groupId, InviteRequest request, HttpContext h,
            DeskAccountStore a, CollaborationStore s, IHubContext<CollaborationHub> hub) =>
        {
            var actor = CurrentUser(h, a)!;
            var target = a.FindUserByIdentifier((request.Identifier ?? "").Trim().ToLowerInvariant());
            if (target is null) return Results.NotFound(new ApiError("Zurvan Desk account not found."));
            var result = s.Invite(groupId, actor.Id, target.Id, request.Role ?? "member");
            await Notify(hub, s, result.Change);
            await hub.Clients.Group(UserChannel(target.Id)).SendAsync("collaborationChanged", result.Change);
            return Results.Ok(result.Value);
        });

        api.MapGet("/invitations", (HttpContext h, DeskAccountStore a, CollaborationStore s) =>
            Results.Ok(s.InvitationsFor(CurrentUser(h, a)!.Id)));

        api.MapPost("/invitations/{inviteId}/accept", async (string inviteId, HttpContext h,
            DeskAccountStore a, CollaborationStore s, IHubContext<CollaborationHub> hub) =>
        {
            var result = s.AcceptInvite(inviteId, CurrentUser(h, a)!.Id);
            await Notify(hub, s, result.Change);
            return Results.Ok(result.Value);
        });

        api.MapPut("/groups/{groupId}/members/{memberId}/permissions", async (
            string groupId, string memberId, MemberPermissions request, HttpContext h,
            DeskAccountStore a, CollaborationStore s, IHubContext<CollaborationHub> hub) =>
        {
            var result = s.UpdatePermissions(groupId, CurrentUser(h, a)!.Id, memberId, request);
            await Notify(hub, s, result.Change);
            return Results.Ok(result.Value);
        });

        api.MapDelete("/groups/{groupId}/members/{memberId}", async (
            string groupId, string memberId, HttpContext h, DeskAccountStore a,
            CollaborationStore s, IHubContext<CollaborationHub> hub) =>
        {
            var result = s.RemoveMember(groupId, CurrentUser(h, a)!.Id, memberId);
            await Notify(hub, s, result.Change);
            await hub.Clients.Group(UserChannel(memberId)).SendAsync("collaborationAccessRevoked",
                new { GroupId = groupId });
            return Results.NoContent();
        });

        api.MapGet("/groups/{groupId}/items", (string groupId, HttpContext h,
            DeskAccountStore a, CollaborationStore s) =>
            Results.Ok(s.Items(groupId, CurrentUser(h, a)!.Id)));

        api.MapPost("/groups/{groupId}/items", async (string groupId, ItemRequest request,
            HttpContext h, DeskAccountStore a, CollaborationStore s, IHubContext<CollaborationHub> hub) =>
        {
            var result = s.CreateItem(groupId, CurrentUser(h, a)!.Id, request);
            await Notify(hub, s, result.Change);
            return Results.Created($"/api/v1/collaboration/groups/{groupId}/items/{result.Value.Id}", result.Value);
        });

        api.MapPut("/groups/{groupId}/items/{itemId}", async (string groupId, string itemId,
            ItemUpdateRequest request, HttpContext h, DeskAccountStore a, CollaborationStore s,
            IHubContext<CollaborationHub> hub) =>
        {
            var result = s.UpdateItem(groupId, CurrentUser(h, a)!.Id, itemId, request);
            await Notify(hub, s, result.Change);
            return Results.Ok(result.Value);
        });

        api.MapGet("/groups/{groupId}/messages", (string groupId, HttpContext h,
            DeskAccountStore a, CollaborationStore s) =>
            Results.Ok(s.Messages(groupId, CurrentUser(h, a)!.Id)));

        api.MapPost("/groups/{groupId}/messages", async (string groupId, MessageRequest request,
            HttpContext h, DeskAccountStore a, CollaborationStore s, IHubContext<CollaborationHub> hub) =>
        {
            var result = s.SendMessage(groupId, CurrentUser(h, a)!.Id, request);
            await Notify(hub, s, result.Change);
            return Results.Ok(result.Value);
        });

        // Reconciliation endpoint: clients retain a cursor and fetch missed changes on reconnect.
        api.MapGet("/groups/{groupId}/changes", (string groupId, long after,
            HttpContext h, DeskAccountStore a, CollaborationStore s) =>
            Results.Ok(s.Changes(groupId, CurrentUser(h, a)!.Id, after)));
    }

    public static DeskUser? CurrentUser(HttpContext h, DeskAccountStore accounts)
    {
        var header = h.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header["Bearer ".Length..].Trim();
        return string.IsNullOrEmpty(token) ? null : accounts.ResolveSession(token);
    }

    internal static string UserChannel(string id) => "collaboration-user:" + id;

    private static async Task Notify(IHubContext<CollaborationHub> hub, CollaborationStore s, CollaborationChange change)
    {
        // Resolve recipients *after* the committed operation. A removed member cannot
        // continue to receive group events through an old SignalR connection.
        foreach (var recipient in s.ActiveMemberIds(change.GroupId))
            await hub.Clients.Group(UserChannel(recipient)).SendAsync("collaborationChanged", change);
    }
}

public sealed class CollaborationHub(DeskAccountStore accounts) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = CollaborationApi.CurrentUser(Context.GetHttpContext()!, accounts);
        if (user is null) { Context.Abort(); return; }
        // No client-controlled group subscription: authorization is recipient-based.
        await Groups.AddToGroupAsync(Context.ConnectionId, CollaborationApi.UserChannel(user.Id));
        await base.OnConnectedAsync();
    }
}

public record GroupCreateRequest(string Name, bool Agenda = true, bool Kanban = false, bool School = false);
public record InviteRequest(string? Identifier, string? Role);
public record MemberPermissions(bool CanChat, bool CanReport, bool CanEdit);
public record ItemRequest(string Title, string Mode, string State, DateTimeOffset? DueAt);
public record ItemUpdateRequest(string Title, string State, DateTimeOffset? DueAt, int ExpectedVersion);
public record MessageRequest(string Body);

public record CollaborationGroup(string Id, string OwnerId, string Name, bool Agenda, bool Kanban,
    bool School, DateTimeOffset CreatedAt);
public record CollaborationMember(string GroupId, string UserId, string Role, MemberPermissions Permissions);
public record CollaborationInvite(string Id, string GroupId, string InviterId,
    string RecipientId, string Role, DateTimeOffset ExpiresAt, bool Accepted = false);
public record CollaborationItem(string Id, string GroupId, string CreatedBy, string Title,
    string Mode, string State, DateTimeOffset? DueAt, int Version, DateTimeOffset UpdatedAt);
public record CollaborationMessage(string Id, string GroupId, string SenderId, string Body, DateTimeOffset SentAt);
public record CollaborationChange(long Sequence, string GroupId, string Kind, string ResourceId,
    string ActorId, DateTimeOffset Timestamp);
public record CollaborationMutation<T>(T Value, CollaborationChange Change);
public record CollaborationChangePage(IReadOnlyList<CollaborationChange> Events, long NextCursor,
    bool ResyncRequired);

public sealed class CollaborationDatabase
{
    public long Sequence { get; set; }
    public List<CollaborationGroup> Groups { get; set; } = [];
    public List<CollaborationMember> Members { get; set; } = [];
    public List<CollaborationInvite> Invites { get; set; } = [];
    public List<CollaborationItem> Items { get; set; } = [];
    public List<CollaborationMessage> Messages { get; set; } = [];
    public List<CollaborationChange> Changes { get; set; } = [];
}

// Single-instance, file-backed development store. Not suitable for multi-instance hosting.
// A transactional PostgreSQL + outbox migration is required before production.
public sealed class CollaborationStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly CollaborationDatabase _db;

    public CollaborationStore()
    {
        var root = Environment.GetEnvironmentVariable("ZURVAN_DESK_ACCOUNT_DATA_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "collaboration.json");
        _db = File.Exists(_path)
            ? JsonSerializer.Deserialize<CollaborationDatabase>(File.ReadAllText(_path)) ?? new()
            : new CollaborationDatabase();
    }

    private T Read<T>(Func<T> query) { lock (_gate) return query(); }
    private T Commit<T>(Func<T> action)
    {
        lock (_gate)
        {
            var result = action();
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_db));
            File.Move(temporary, _path, true);
            return result;
        }
    }

    private CollaborationGroup Group(string id)
        => _db.Groups.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
    private CollaborationMember Member(string groupId, string userId)
        => _db.Members.FirstOrDefault(x => x.GroupId == groupId && x.UserId == userId)
            ?? throw new UnauthorizedAccessException();
    private static bool Manager(CollaborationMember m) => m.Role is "owner" or "assistant";
    private CollaborationChange Change(string group, string kind, string resource, string actor)
    {
        var e = new CollaborationChange(++_db.Sequence, group, kind, resource, actor, DateTimeOffset.UtcNow);
        _db.Changes.Add(e);
        if (_db.Changes.Count > 20000) _db.Changes.RemoveRange(0, _db.Changes.Count - 20000);
        return e;
    }

    public IReadOnlyList<string> ActiveMemberIds(string groupId) =>
        Read(() => _db.Members.Where(x => x.GroupId == groupId).Select(x => x.UserId).ToArray());

    public IReadOnlyList<CollaborationGroup> GroupsFor(string user) =>
        Read(() => _db.Groups.Where(g => _db.Members.Any(m => m.GroupId == g.Id && m.UserId == user)).ToArray());

    public object GroupDetails(string groupId, string user) =>
        Read(() =>
        {
            var group = Group(groupId);
            Member(groupId, user);
            return new { Group = group, Members = _db.Members.Where(x => x.GroupId == groupId).ToArray() };
        });

    public CollaborationMutation<CollaborationGroup> CreateGroup(string user, GroupCreateRequest request) => Commit(() =>
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
            throw new ArgumentException("Group name must contain 1-120 characters.");
        if (!request.Agenda && !request.Kanban && !request.School)
            throw new ArgumentException("Enable at least one workspace mode.");
        var group = new CollaborationGroup("grp_" + Guid.NewGuid().ToString("N"), user,
            request.Name.Trim(), request.Agenda, request.Kanban, request.School, DateTimeOffset.UtcNow);
        _db.Groups.Add(group);
        _db.Members.Add(new(group.Id, user, "owner", new(true, true, true)));
        return new CollaborationMutation<CollaborationGroup>(group, Change(group.Id, "group.created", group.Id, user));
    });

    public CollaborationMutation<CollaborationInvite> Invite(string groupId, string actor,
        string target, string role) => Commit(() =>
    {
        var m = Member(groupId, actor);
        if (!Manager(m)) throw new UnauthorizedAccessException();
        if (role is not ("member" or "assistant")) throw new ArgumentException("Invalid role.");
        if (role == "assistant" && m.Role != "owner") throw new UnauthorizedAccessException();
        if (Group(groupId).OwnerId == target || _db.Members.Any(x => x.GroupId == groupId && x.UserId == target))
            throw new InvalidOperationException("User is already a group member.");
        if (_db.Invites.Any(x => x.GroupId == groupId && x.RecipientId == target &&
            !x.Accepted && x.ExpiresAt > DateTimeOffset.UtcNow))
            throw new InvalidOperationException("An active invitation already exists.");
        var invite = new CollaborationInvite("inv_" + Guid.NewGuid().ToString("N"),
            groupId, actor, target, role, DateTimeOffset.UtcNow.AddDays(7));
        _db.Invites.Add(invite);
        return new CollaborationMutation<CollaborationInvite>(invite,
            Change(groupId, "invitation.created", invite.Id, actor));
    });

    public IReadOnlyList<CollaborationInvite> InvitationsFor(string user) =>
        Read(() => _db.Invites.Where(x => x.RecipientId == user && !x.Accepted &&
            x.ExpiresAt > DateTimeOffset.UtcNow).ToArray());

    public CollaborationMutation<CollaborationMember> AcceptInvite(string id, string user) => Commit(() =>
    {
        var i = _db.Invites.FirstOrDefault(x => x.Id == id && x.RecipientId == user)
            ?? throw new KeyNotFoundException();
        if (i.Accepted || i.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Invitation expired or already accepted.");
        if (_db.Members.Any(x => x.GroupId == i.GroupId && x.UserId == user))
            throw new InvalidOperationException("Already a member.");
        // Accepting does not rely on the invitation sender remaining a member, but
        // owner must still exist. Assistant privileges require a valid owner-issued invite.
        var owner = Group(i.GroupId).OwnerId;
        if (i.Role == "assistant" && i.InviterId != owner)
            throw new UnauthorizedAccessException();
        var m = new CollaborationMember(i.GroupId, user, i.Role, new(true, true, false));
        _db.Members.Add(m);
        _db.Invites.Remove(i);
        return new CollaborationMutation<CollaborationMember>(m,
            Change(i.GroupId, "member.joined", user, user));
    });

    public CollaborationMutation<CollaborationMember> UpdatePermissions(string groupId,
        string actor, string target, MemberPermissions permissions) => Commit(() =>
    {
        var manager = Member(groupId, actor);
        if (!Manager(manager)) throw new UnauthorizedAccessException();
        var existing = Member(groupId, target);
        if (existing.Role == "owner" || (existing.Role == "assistant" && manager.Role != "owner"))
            throw new UnauthorizedAccessException();
        var updated = existing with { Permissions = permissions };
        _db.Members[_db.Members.IndexOf(existing)] = updated;
        return new CollaborationMutation<CollaborationMember>(updated,
            Change(groupId, "member.permissions", target, actor));
    });

    public CollaborationMutation<string> RemoveMember(string groupId, string actor, string target) => Commit(() =>
    {
        var actorMember = Member(groupId, actor);
        var existing = Member(groupId, target);
        if (existing.Role == "owner") throw new UnauthorizedAccessException();
        if (actor != target && (!Manager(actorMember) ||
            (existing.Role == "assistant" && actorMember.Role != "owner")))
            throw new UnauthorizedAccessException();
        _db.Members.Remove(existing);
        _db.Invites.RemoveAll(x => x.GroupId == groupId && x.RecipientId == target);
        return new CollaborationMutation<string>(target, Change(groupId, "member.removed", target, actor));
    });

    public IReadOnlyList<CollaborationItem> Items(string groupId, string actor) =>
        Read(() => { Member(groupId, actor); return _db.Items.Where(x => x.GroupId == groupId).ToArray(); });

    public CollaborationMutation<CollaborationItem> CreateItem(string groupId, string actor,
        ItemRequest request) => Commit(() =>
    {
        var g = Group(groupId);
        var m = Member(groupId, actor);
        if (!m.Permissions.CanEdit && !Manager(m)) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 240)
            throw new ArgumentException("Title must contain 1-240 characters.");
        if (!((request.Mode == "agenda" && g.Agenda) ||
              (request.Mode == "kanban" && g.Kanban) ||
              (request.Mode == "school" && g.School))) throw new ArgumentException("Mode is disabled.");
        if (request.State is not ("todo" or "doing" or "done"))
            throw new ArgumentException("Invalid item state.");
        var item = new CollaborationItem("item_" + Guid.NewGuid().ToString("N"), groupId, actor,
            request.Title.Trim(), request.Mode, request.State, request.DueAt, 1, DateTimeOffset.UtcNow);
        _db.Items.Add(item);
        return new CollaborationMutation<CollaborationItem>(item,
            Change(groupId, "item.created", item.Id, actor));
    });

    public CollaborationMutation<CollaborationItem> UpdateItem(string groupId, string actor,
        string itemId, ItemUpdateRequest request) => Commit(() =>
    {
        var m = Member(groupId, actor);
        if (!m.Permissions.CanEdit && !Manager(m)) throw new UnauthorizedAccessException();
        var existing = _db.Items.FirstOrDefault(x => x.GroupId == groupId && x.Id == itemId)
            ?? throw new KeyNotFoundException();
        if (existing.Version != request.ExpectedVersion)
            throw new InvalidOperationException("Version conflict: reload and retry.");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 240 ||
            request.State is not ("todo" or "doing" or "done"))
            throw new ArgumentException("Invalid item title or state.");
        var updated = existing with { Title = request.Title.Trim(), State = request.State,
            DueAt = request.DueAt, Version = existing.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
        _db.Items[_db.Items.IndexOf(existing)] = updated;
        return new CollaborationMutation<CollaborationItem>(updated,
            Change(groupId, "item.updated", itemId, actor));
    });

    public IReadOnlyList<CollaborationMessage> Messages(string groupId, string actor) =>
        Read(() => { Member(groupId, actor); return _db.Messages.Where(x => x.GroupId == groupId)
            .OrderByDescending(x => x.SentAt).Take(100).Reverse().ToArray(); });

    public CollaborationMutation<CollaborationMessage> SendMessage(string groupId, string actor,
        MessageRequest request) => Commit(() =>
    {
        var m = Member(groupId, actor);
        if (!m.Permissions.CanChat) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 4000)
            throw new ArgumentException("Message must contain 1-4000 characters.");
        var msg = new CollaborationMessage("msg_" + Guid.NewGuid().ToString("N"),
            groupId, actor, request.Body.Trim(), DateTimeOffset.UtcNow);
        _db.Messages.Add(msg);
        return new CollaborationMutation<CollaborationMessage>(msg,
            Change(groupId, "message.created", msg.Id, actor));
    });

    public CollaborationChangePage Changes(string groupId, string actor, long after) => Read(() =>
    {
        Member(groupId, actor);
        var available = _db.Changes.Where(x => x.GroupId == groupId).OrderBy(x => x.Sequence).ToArray();
        var first = available.FirstOrDefault()?.Sequence ?? 0;
        var events = available.Where(x => x.Sequence > after).Take(100).ToArray();
        return new CollaborationChangePage(events, events.LastOrDefault()?.Sequence ?? after,
            after > 0 && first > 0 && after < first - 1);
    });
}
