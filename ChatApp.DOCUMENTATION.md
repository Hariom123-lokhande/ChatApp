# ChatApp - End-to-End Architecture & Data Flow

Ye document project ka concise aur detailed architecture, end-to-end data flow aur important files ka role describe karta hai. (Razor Views based web app with SignalR realtime chat)

---

## 1. High-level overview

- App type: ASP.NET Core (Razor Views + MVC controllers) backend + SignalR realtime hub.
- Main responsibilities:
  - Authentication (JWT + cookie)
  - Real-time messaging (SignalR hub `ChatHub`)
  - Persistent storage via `AppDbContext` (SQL Server)
  - Client UI in `Views/Home/Chat.cshtml` and `wwwroot/js/*.js`
  - Group management, notifications, read receipts
  - Localization (Resources) and session support

---

## 2. Startup and middleware (`Program.cs`)

- `AddDbContext<AppDbContext>`: SQL Server connection configured via `DefaultConnection`.
- `AddSingleton<IConnectionManager, ConnectionManager>`: in-memory connection tracking for SignalR.
- `AddSignalR(...)`: SignalR configuration (message size, keep-alive, timeouts).
- `AddCors("AllowFrontend")`: CORS policy from configuration.
- `AddAuthentication().AddJwtBearer(...)`: JWT configured with issuer/audience/key from `appsettings.json`.
  - Bearer tokens are read from header, query param `access_token` (for websockets) and cookie `chat_token`.
- `AddControllersWithViews()` and localization configuration.
- `AddSession()` and `AddDistributedMemoryCache()` for server-side session support.
- Routes:
  - MVC default route → `HomeController.Chat` serves the main UI (`/` → Home/Chat).
  - SignalR hub mapped at `/chatHub`.
  - Health checks at `/health`.
- On startup: reset `Users.IsOnline` to `false` to avoid stale online states.

Flow: browser requests → static files served → MVC route serves `Chat.cshtml` → frontend initializes SignalR and APIs.

---

## 3. Database & Entities (`AppDbContext` + `ChatApp.Domain.Entities`)

Key tables/entities and relationships:

- `User`
  - `UserId` (PK), `Username`, `Email`, `PasswordHash`, `IsOnline`, `LastSeen`, audit fields
  - Relations: SentMessages, ReceivedMessages, GroupMemberships, MessageReadStatuses, Notifications

- `Message`
  - `MessageId` (PK), `RequestId` (unique for idempotency), `SenderId`, `ReceiverId` (private), `GroupId` (group), `Content`, `DeliveryStatus` (0=sent/undelivered,1=delivered,2=read), `MessageType`
  - Indexes for sender/receiver/group/createdAt and composite indexes for efficient history queries

- `Group`
  - `GroupId`, `Name`, `CreatedBy`, description, messages, members

- `GroupMember`
  - `Id`, `GroupId`, `UserId`, `Role` (Admin/Member), `JoinedAt`
  - Unique index on `{GroupId,UserId}`

- `MessageReadStatus`
  - Per-user per-message read state; unique `{MessageId,UserId}`

- `Notification`
  - `Id`, `UserId`, `MessageId`, `Type`, `Content`, `IsRead`

Global query filters enforce `IsDeleted = false` across entities (soft delete).

Data flow example for a private message being sent:
1. Client calls SignalR hub method `SendPrivateMessage` (or REST fallback).
2. `ChatHub.SendPrivateMessage` validates, checks rate-limits and idempotency (`RequestId`).
3. New `Message` entity created with `DeliveryStatus = 0` and saved to DB.
4. `MessageReadStatus` and `Notification` entries created for the receiver via `SaveMeta`.
5. SignalR `Send` attempts to deliver to receiver's active connections. If online, client receives `ReceivePrivateMessage`.
6. When receiver's client displays message it calls `MessageDelivered` (or hub ack). Hub updates `DeliveryStatus = 1` and notifies sender's connections.
7. On read, client calls `MarkMessagesRead`, which sets `IsRead=true` in `MessageReadStatus`, updates `DeliveryStatus = 2` on message and notifies sender with `MessageRead`.

Group message flow is similar but uses SignalR groups and creates `MessageReadStatus`/`Notification` entries for each member (excluding sender).

---

## 4. Real-time layer: `ChatHub` (SignalR)

File: `ChatApp.API/Hubs/ChatHub.cs`

Responsibilities:
- Require `[Authorize]` (JWT via cookie/query/header).
- Connection lifecycle:
  - `OnConnectedAsync`: retrieve user id & username from claims, register connection in `IConnectionManager`, send current online users list to caller, deliver undelivered messages (DeliveryStatus == 0) to connecting user, mark them online, add them to any SignalR groups based on `GroupMembers`.
  - `OnDisconnectedAsync`: remove connection, wait (delay) to allow transient reconnect, then mark offline if no active connections remain and notify others.

- Messaging methods:
  - `SendPrivateMessage(SendPrivateMessageRequest r)`: validate, rate-limit, idempotency by `RequestId`, create `Message`, save meta (read-status + notification), persist, send to receiver (via `Send` helper), and echo `MessageSent` to sender.
  - `MessageDelivered(Guid mid)`: mark message delivered and notify sender's connections.
  - `SendGroupMessage(SendGroupMessageRequest r)`: similar to private message but persists one `Message` and per-member read-status + notification entries; sends to SignalR group with `Clients.Group`.
  - `FetchMissedMessages(DateTime last)`: used for reconnect to fetch missed messages since `last` timestamp and mark delivered if private and undelivered.
  - `MarkMessagesRead(List<Guid> mids)`: set `IsRead` flags and notify original senders with `MessageRead` events.
  - Typing indicators: `SendTyping`, `StopTyping` → broadcasts to other party (private) or group members.

Helpers:
- `Send(Guid uid, string ev, object data)`: uses `IConnectionManager.GetConnections` to send events to a user's active connections.
- `Create`, `Map`, `SaveMeta`: create message entity, map to DTO, and persist read/notification metadata.
- Rate limiting: in-memory `_rate` dictionary with per-user timestamps; configured via `Security:MaxMessageRatePerSecond`.

Security considerations:
- Hub methods rely on the JWT claims for user id/name.
- Input is HTML-encoded via `HttpUtility.HtmlEncode` before saving.

---

## 5. Authentication & Authorization (Controllers)

- `AuthController` (`ChatApp.API/controllers/AuthController.cs`)
  - `POST /api/auth/register`: server-side password policy validation (configurable via `Security` section), uniqueness checks, password hashing using `BCrypt`, creates `User` record.
  - `POST /api/auth/login`: verifies credentials, issues JWT token (signed with `Jwt:Key`), sets cookie `chat_token` (HttpOnly), stores `UserId` in session, updates user's `LastSeen`.
  - `POST /api/auth/logout`: deletes cookie and clears session.
  - `GET /api/auth/verify`: returns token info if authorized.

- `UserController` (`ChatApp.API/controllers/UserController.cs`)
  - Requires `[Authorize]`.
  - `GET /api/users`: returns all users except current with online state (from `IConnectionManager`).
  - `GET /api/users/{id}`: user profile.
  - `GET /api/users/search?query=`: search users.
  - `PUT /api/users/profile`: update current user's username/profile picture.
  - Notification endpoints: list and mark-as-read.

- `GroupController` (`ChatApp.API/controllers/GroupController.cs`)
  - Create group, add/remove members, add by email, get group details, list my groups, update and delete group.
  - When adding by email, if target is online, controller uses `IHubContext<ChatHub>` to `SendAsync("AddedToGroup", ...)` to notify target and add their SignalR connections to the SignalR group.

---

## 6. Connection management (`ConnectionManager`) - in-memory service

File: `ChatApp.Application/Services/ConnectionManager.cs`

- Maintains two concurrent dictionaries:
  - `_userConnections: ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>>` → maps userId to set of connectionIds.
  - `_connectionUsers: ConcurrentDictionary<string, Guid>` → reverse lookup.
- Methods:
  - `AddConnection(userId, connectionId)`
  - `RemoveConnection(connectionId)`
  - `GetConnections(userId)` → list of connection ids
  - `IsOnline(userId)` → true if has active connections
  - `GetAllOnlineUsers()` → list of user ids currently online

Role in data flow: Hub registers connections on connect and removes on disconnect; controllers/hub use it to determine online users and to send targeted messages.

---

## 7. Frontend (Razor view + JS)

Main view: `ChatApp.API/Views/Home/Chat.cshtml`
- Serves the full chat UI and loads client scripts and CSS:
  - `wwwroot/css/chat.css` — styles.
  - `wwwroot/js/main.js` — main entry (type=module) that sets up authentication state, SignalR connection and wires UI.
  - `wwwroot/js/signalr.js` — thin SignalR helper (likely wraps hub connection creation and reconnection logic).
  - `wwwroot/js/api.js` — REST API wrapper for controllers (`/api/auth`, `/api/users`, `/api/groups` etc.).
  - `wwwroot/js/auth.js` — login/register UI handlers.
  - `wwwroot/js/chat-core.js` — core chat logic: send/receive events, message rendering, delivery/read handling.
  - `wwwroot/js/chat-ui.js` — DOM helpers and UI interactions.
  - `wwwroot/js/state.js` — client-side state management (current user, active chat, caches).

Client -> Server flows:
- Authentication: `auth.js` calls `/api/auth/login` → server sets `chat_token` cookie and returns token+user info. Client stores minimal state in `state.js` and opens SignalR connection with the cookie/token.
- SignalR startup: `main.js`/`signalr.js` initializes HubConnection that sends `access_token` query param when using WebSockets if needed. On connect, server runs `OnConnectedAsync` which pushes undelivered messages and online users.
- Sending message: UI triggers `chat-core.js` to call Hub method `SendPrivateMessage` or `SendGroupMessage` with unique `RequestId`. On success client receives `MessageSent`, otherwise error.
- Delivery/read lifecycle: client calls `MessageDelivered` and `MarkMessagesRead` hub methods; UI updates message status icons accordingly.
- Groups: creation and member management use `/api/groups` endpoints; controller notifies added members via `_hub.Clients` and binds their connections to SignalR groups server-side.

Note: Actual JS file names exist in the repository (open files list). Open each for exact implementation details; above is expected roles based on naming.

---

## 8. Security & Configuration

- JWT settings: `Jwt:Issuer`, `Jwt:Audience`, `Jwt:Key`, `DurationInMinutes` in `appsettings.json`.
- Security password policy under `Security` section (min length, require uppercase/lowercase/digit/special char, max message rate per second).
- `chat_token` cookie: HttpOnly; used by SignalR server bearer handling and browser requests.
- Session used to store `UserId` for same-browser-tab convenience.
- Input sanitization: messages are HTML-encoded before saving via `HttpUtility.HtmlEncode`.
- Rate limiting: in-memory per-user rate limiter in `ChatHub` with configurable `_limit`.

---

## 9. Important files (per-file short summary)

- `ChatApp.API/Program.cs` — app startup, middleware, DI, map hub, localization, session.
- `ChatApp.API/Hubs/ChatHub.cs` — SignalR hub: all realtime methods, connection lifecycle, message flows.
- `ChatApp.Application/Services/ConnectionManager.cs` — in-memory connection tracking.
- `ChatApp.Infrastructure/Data/AppDbContext.cs` — EF Core DB context + model configuration and query filters.
- `ChatApp.API/controllers/AuthController.cs` — register/login/logout and token verification.
- `ChatApp.API/controllers/UserController.cs` — user listing/profile/notifications.
- `ChatApp.API/controllers/GroupController.cs` — group CRUD + member management.
- `ChatApp.Domain/Entities/*.cs` — entities: `User`, `Message`, `Group`, `GroupMember`, `MessageReadStatus`, `Notification`.
- `ChatApp.API/Views/Home/Chat.cshtml` — main Razor View that hosts the SPA-like chat UI; includes localization dropdown and loads scripts.
- `ChatApp.API/wwwroot/js/*.js` — frontend scripts (auth, main, signalr wrapper, api wrapper, chat UI/core, state management).
- `ChatApp.API/wwwroot/css/chat.css` — styles for the chat interface.
- `ChatApp.API/Resources/SharedResource.resx` — localization resource strings.
- `ChatApp.Infrastructure/Migrations/*` — EF Core migrations for DB schema.

---

## 10. Typical sequence: Sign-in → Chat → Read/Notify (end-to-end)

1. User opens `/` → server returns `Chat.cshtml`.
2. On first use the UI shows auth screen; user registers or logs in.
3. On successful login, server sets `chat_token` cookie and returns user info.
4. Frontend initializes SignalR hub, connecting with cookie/token.
5. Hub `OnConnectedAsync` registers connection, marks user online and pushes undelivered messages.
6. User selects a contact or group, message compose → client sends via `SendPrivateMessage` / `SendGroupMessage`.
7. Server persists message and meta, attempts delivery via connections or SignalR groups.
8. Recipient's client acknowledges delivery and read events; server updates `DeliveryStatus` and read statuses and notifies sender.
9. Notifications are stored and can be fetched via `/api/users/notifications`.

---

## 11. Where to extend or inspect more closely

- To change message retention or add encryption: modify `Message` entity and `ChatHub.Create/Map` logic.
- To add push notifications: create background worker to push `Notification` entries to external service and trigger from `SaveMeta`.
- To persist presence across servers: replace `ConnectionManager` with a distributed store (Redis) and make SignalR scale-out.
- To secure cookies for production: set `Secure=true` and consider `SameSite=None` when using cross-site frontends.

---

## 12. Next steps I can do for you (examples)
- Generate an internal sequence diagram (PlantUML) for message flow.
- Expand documentation with content of each frontend JS file (`wwwroot/js/*`) showing exact functions and events.
- Add README with developer setup steps (DB, migrations, appsettings).


---

Generated by: automatic analysis of workspace files. If you want, I can expand any section or produce per-file detailed call graphs.
