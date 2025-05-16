using Google.Apis.Gmail.v1;

namespace GmailCleaner.Configuration
{
    public static class GmailConfiguration
    {
        // Scopes determine the level of access the application has.
        public static readonly string[] Scopes = { GmailService.Scope.GmailModify };
        public static readonly string ApplicationName = "GmailThreadCleaner";
        public static readonly string ClientSecretFileName = "client_secret.json";
        public static readonly string UserCredentialTokenFileName = "gmail-dotnet-token.json";

        // --- Parallelism Settings ---
        // Max threads to mark as read concurrently WITHIN a single page's processing.
        public static readonly int MaxConcurrentItemOperations = 10;
        // Max pages to have their items processed concurrently.
        public static readonly int MaxConcurrentPageProcessingTasks = 3;
        // Max threads to fetch in a single page listing.
        public static readonly int MaxResultsPerPage = 100;
    }
}
