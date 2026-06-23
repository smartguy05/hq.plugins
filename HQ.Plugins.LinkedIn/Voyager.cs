using System.Net;

namespace HQ.Plugins.LinkedIn;

/// <summary>
/// Builders for LinkedIn's internal Voyager API paths used by the web client. These
/// endpoints are <b>unofficial and version-drift-prone</b> — LinkedIn renames/relocates
/// them periodically. They are deliberately centralized here so that when the live web
/// client changes (observable in the browser Network tab), only this file needs updating;
/// the service and tests stay stable. All builders return a path relative to the LinkedIn
/// origin, ready for <see cref="ILinkedInBrowser.VoyagerAsync"/>.
/// </summary>
public static class Voyager
{
    private static string Enc(string s) => WebUtility.UrlEncode(s ?? "");

    // ---- Reads ----

    /// <summary>Current member (used as an auth probe).</summary>
    public const string Me = "/voyager/api/me";

    /// <summary>Full profile view for a public identifier (the slug in linkedin.com/in/{slug}).</summary>
    public static string ProfileView(string publicId) =>
        $"/voyager/api/identity/profiles/{Enc(publicId)}/profileView";

    /// <summary>Lightweight typeahead search. <paramref name="type"/> is PEOPLE or COMPANY.</summary>
    public static string Typeahead(string keywords, string type) =>
        $"/voyager/api/typeahead/hitsV2?keywords={Enc(keywords)}&origin=GLOBAL_SEARCH_HEADER&q=type&type={Enc(type)}";

    /// <summary>Company by its universal name (the slug in linkedin.com/company/{slug}).</summary>
    public static string Company(string universalName) =>
        $"/voyager/api/organization/companies?q=universalName&universalName={Enc(universalName)}";

    /// <summary>The member's conversation list.</summary>
    public const string Conversations = "/voyager/api/messaging/conversations";

    /// <summary>Events (messages) within a conversation.</summary>
    public static string ConversationEvents(string conversationId) =>
        $"/voyager/api/messaging/conversations/{Enc(conversationId)}/events";

    /// <summary>Premium entitlements (InMail balance lives here).</summary>
    public const string Entitlements = "/voyager/api/voyagerPremiumDashEntitlements?q=entitlements";

    // ---- Writes (action endpoints) ----

    /// <summary>Post an event (message) into an existing conversation.</summary>
    public static string SendMessage(string conversationId) =>
        $"/voyager/api/messaging/conversations/{Enc(conversationId)}/events?action=create";

    /// <summary>Create a new conversation (start a chat).</summary>
    public const string CreateConversation = "/voyager/api/messaging/conversations?action=create";

    /// <summary>Send a connection invitation.</summary>
    public const string Invitations = "/voyager/api/growth/normInvitations";

    /// <summary>Create a share (post).</summary>
    public const string Shares = "/voyager/api/contentcreation/normShares";

    /// <summary>Comment on a post/activity.</summary>
    public static string Comments(string threadUrn) =>
        $"/voyager/api/feed/comments?threadUrn={Enc(threadUrn)}";

    /// <summary>React to a post/activity.</summary>
    public static string Reactions(string threadUrn) =>
        $"/voyager/api/voyagerSocialDashReactions?threadUrn={Enc(threadUrn)}";
}
