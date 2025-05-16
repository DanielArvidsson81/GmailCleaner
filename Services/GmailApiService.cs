#nullable enable

using GmailCleaner.Configuration;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace GmailCleaner.Services
{
    public class GmailApiService
    {
        private readonly GmailService _service;
        private long _totalUnreadThreadsEstimate; // Stores the initial estimate of unread threads.
        private int _overallThreadsMarkedSuccessfullyCounter; // Tracks total successful operations.

        private GmailApiService(GmailService service)
        {
            _service = service;
        }

        /// <summary>
        /// Creates and initializes an instance of the GmailApiService.
        /// Handles authentication and Gmail service client setup.
        /// </summary>
        /// <returns>A Task that resolves to a GmailApiService instance, or null if initialization fails.</returns>
        public static async Task<GmailApiService?> CreateAsync()
        {
            Console.WriteLine("[Auth] Attempting to authorize and initialize Gmail service...");
            UserCredential? credential;
            try
            {
                var clientSecrets = await LoadClientSecretsAsync();
                if (clientSecrets == null) return null;

                credential = await AuthorizeAsync(clientSecrets);
                // Assuming AuthorizeAsync provides a valid credential or throws an exception on failure.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Critical error during authentication setup: {ex.Message}");
                return null;
            }

            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = GmailConfiguration.ApplicationName,
            });
            Console.WriteLine("[Auth] Gmail service initialized successfully.");
            return new GmailApiService(service);
        }

        /// <summary>
        /// Loads client secrets from the specified JSON file.
        /// </summary>
        private static async Task<ClientSecrets?> LoadClientSecretsAsync()
        {
            try
            {
                var secrets = await GoogleClientSecrets.FromFileAsync(GmailConfiguration.ClientSecretFileName);
                if (secrets?.Secrets == null)
                {
                    Console.WriteLine($"[Auth] Error: '{GmailConfiguration.ClientSecretFileName}' is invalid or empty.");
                    return null;
                }
                return secrets.Secrets;
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"[Auth] Error: '{GmailConfiguration.ClientSecretFileName}' not found. Please download it from Google Cloud Console and place it in the application directory.");
                return null;
            }
        }

        /// <summary>
        /// Authorizes the application to access Gmail data using OAuth 2.0.
        /// Stores and retrieves access tokens.
        /// </summary>
        private static async Task<UserCredential> AuthorizeAsync(ClientSecrets clientSecrets)
        {
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string credFileDir = Path.Combine(appDataFolder, GmailConfiguration.ApplicationName.Replace(" ", ""));
            Directory.CreateDirectory(credFileDir);
            string credPath = Path.Combine(credFileDir, GmailConfiguration.UserCredentialTokenFileName);

            Console.WriteLine($"[Auth] User credential token will be stored/loaded from: {credPath}");
            // GoogleWebAuthorizationBroker.AuthorizeAsync handles the OAuth flow,
            // including opening a browser for user consent if needed.
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                GmailConfiguration.Scopes,
                "user", // User identifier for storing credentials in the FileDataStore.
                CancellationToken.None,
                new FileDataStore(credPath, true));
        }

        /// <summary>
        /// Main public method to start the process of marking all unread threads as read.
        /// </summary>
        public async Task MarkAllUnreadThreadsAsReadAsync()
        {
            if (!await InitializeUnreadCountAsync())
            {
                // If initialization indicates no work or an early exit condition.
                return;
            }

            await FetchAndProcessUnreadThreadsPipelinedAsync();
            LogFinalSummary();
        }

        /// <summary>
        /// Fetches the initial total count of unread threads.
        /// </summary>
        /// <returns>True if processing should continue, false otherwise (e.g., no unread threads).</returns>
        private async Task<bool> InitializeUnreadCountAsync()
        {
            Console.WriteLine("\n[Count] Fetching total unread thread count...");
            try
            {
                var unreadLabelInfo = await _service.Users.Labels.Get("me", "UNREAD").ExecuteAsync();
                _totalUnreadThreadsEstimate = unreadLabelInfo.ThreadsUnread ?? 0;
                Console.WriteLine($"[Count] Found {_totalUnreadThreadsEstimate} unread threads (conversations) in total.");
                if (_totalUnreadThreadsEstimate == 0)
                {
                    Console.WriteLine("[Count] No unread threads to process.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Count] Warning: Could not retrieve total unread count: {ex.Message}. Proceeding without exact total.");
                _totalUnreadThreadsEstimate = -1; // Indicate that the total count is unknown.
                return true; // Still attempt to process threads page by page.
            }
        }

        /// <summary>
        /// Fetches a single page of unread thread IDs from Gmail.
        /// </summary>
        /// <param name="pageToken">The token for the next page, or null for the first page.</param>
        private async Task<ListThreadsResponse> FetchThreadsPageAsync(string? pageToken)
        {
            var listRequest = _service.Users.Threads.List("me");
            listRequest.Q = "is:unread"; // Gmail query to filter for unread threads.
            listRequest.MaxResults = GmailConfiguration.MaxResultsPerPage;
            listRequest.PageToken = pageToken; // API handles null pageToken for the first page.
            return await listRequest.ExecuteAsync();
        }

        /// <summary>
        /// Orchestrates fetching pages of threads and dispatching them for concurrent item processing.
        /// This method implements a pipelined approach: fetching the next page can overlap
        /// with processing items from the current page.
        /// </summary>
        private async Task FetchAndProcessUnreadThreadsPipelinedAsync()
        {
            Console.WriteLine("\n[Processing] Starting to mark threads as read...");
            string? currentPageToken = null;
            int pageNumber = 1;
            // overallThreadsFetchedFromPages can be used in the summary if needed to show total items encountered.
            // int overallThreadsFetchedFromPages = 0;
            var activePageProcessingTasks = new List<Task>();

            // Semaphore to limit how many pages can have their items processed concurrently.
            using (var pageProcessingSemaphore = new SemaphoreSlim(GmailConfiguration.MaxConcurrentPageProcessingTasks))
            {
                do
                {
                    Console.WriteLine($"\n[Page {pageNumber}] Fetching data...");
                    ListThreadsResponse pageResponse;
                    try
                    {
                        pageResponse = await FetchThreadsPageAsync(currentPageToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Page {pageNumber}] Error fetching data: {ex.Message}. Skipping this page fetch attempt.");
                        currentPageToken = null; // Stop fetching if a page retrieval fails critically.
                        continue;
                    }

                    var threadsOnThisPage = pageResponse.Threads;
                    if (threadsOnThisPage != null && threadsOnThisPage.Any())
                    {
                        int numThreadsOnPage = threadsOnThisPage.Count;
                        // overallThreadsFetchedFromPages += numThreadsOnPage;
                        var threadsToProcessCopy = threadsOnThisPage.ToList(); // Copy for safe closure in the task.

                        await pageProcessingSemaphore.WaitAsync(); // Wait for a slot to process this page's items.
                        Console.WriteLine($"[Page {pageNumber}] Fetched {numThreadsOnPage} threads. Queueing for item processing.");

                        // Offload the processing of this page's items to a separate task.
                        var pageTask = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessItemsOnPageConcurrentlyAsync(threadsToProcessCopy, pageNumber, numThreadsOnPage);
                            }
                            finally
                            {
                                pageProcessingSemaphore.Release(); // Release the slot when done.
                            }
                        });
                        activePageProcessingTasks.Add(pageTask);
                    }
                    else
                    {
                        LogNoMoreThreadsFound(pageNumber);
                        break; // No more threads found, exit loop.
                    }

                    currentPageToken = pageResponse.NextPageToken;
                    pageNumber++;
                    // Clean up completed tasks from the list to prevent indefinite growth.
                    activePageProcessingTasks.RemoveAll(t => t.IsCompleted);

                } while (!string.IsNullOrEmpty(currentPageToken)); // Loop until no more pages.

                Console.WriteLine("\n[Processing] All pages fetched. Waiting for remaining item processing tasks to complete...");
                await Task.WhenAll(activePageProcessingTasks); // Ensure all dispatched page processing finishes.
            }
        }

        /// <summary>
        /// Processes all threads from a single fetched page concurrently.
        /// </summary>
        /// <param name="threads">List of threads from the current page.</param>
        /// <param name="pageNum">The current page number (for logging).</param>
        /// <param name="totalOnPage">Total number of threads on this page (for logging).</param>
        private async Task ProcessItemsOnPageConcurrentlyAsync(List<Google.Apis.Gmail.v1.Data.Thread> threads, int pageNum, int totalOnPage)
        {
            Console.WriteLine($"  [Page {pageNum}] Starting item processing for {threads.Count} threads...");

            // Semaphore to limit concurrent "mark as read" operations within this page.
            using var itemSemaphore = new SemaphoreSlim(GmailConfiguration.MaxConcurrentItemOperations);
            var itemTasks = threads.Select(thread =>
                ProcessSingleThreadAsync(thread, itemSemaphore, pageNum, totalOnPage)
            ).ToList();

            bool[] results = await Task.WhenAll(itemTasks); // Get success/failure for each item.
            int itemsMarkedSuccessfullyOnThisPage = results.Count(success => success);
            Console.WriteLine($"  [Page {pageNum}] Finished item processing. Marked {itemsMarkedSuccessfullyOnThisPage}/{threads.Count} on this page.");
        }

        /// <summary>
        /// Marks a single Gmail thread as read.
        /// </summary>
        /// <returns>True if the thread was successfully marked as read, false otherwise.</returns>
        private async Task<bool> ProcessSingleThreadAsync(
            Google.Apis.Gmail.v1.Data.Thread thread,
            SemaphoreSlim semaphore,
            int pageNum,
            int totalOnPage)
        {
            await semaphore.WaitAsync(); // Wait for a slot to process this item.
            bool success = false;
            try
            {
                // To mark as read, we remove the "UNREAD" label from the thread.
                var modifyRequest = new ModifyThreadRequest { RemoveLabelIds = new List<string> { "UNREAD" } };
                // Assuming thread.Id is non-null if the 'thread' object itself is provided by the API.
                // Using null-forgiving operator '!' as a declaration of this assumption.
                await _service.Users.Threads.Modify(modifyRequest, "me", thread.Id!).ExecuteAsync();

                int currentOverallMarked = Interlocked.Increment(ref _overallThreadsMarkedSuccessfullyCounter);
                string totalEstStr = _totalUnreadThreadsEstimate >= 0 ? _totalUnreadThreadsEstimate.ToString() : "?";
                Console.WriteLine($"    [Page {pageNum}] (Overall: {currentOverallMarked}/~{totalEstStr}) Marked: {thread.Id!}");
                success = true;
            }
            catch (Google.GoogleApiException apiEx)
            {
                Console.WriteLine($"    [Page {pageNum}] !! Failed Thread ID: {thread.Id ?? "Unknown"}. API Error: {apiEx.Error?.Message} (Code: {apiEx.Error?.Code})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    [Page {pageNum}] !! Failed Thread ID: {thread.Id ?? "Unknown"}. Error: {ex.Message}");
            }
            finally
            {
                semaphore.Release(); // Release the item processing slot.
            }
            return success;
        }

        /// <summary>
        /// Logs a message indicating why no more threads are being processed.
        /// </summary>
        private void LogNoMoreThreadsFound(int pageNumber)
        {
            if (pageNumber == 1 && _totalUnreadThreadsEstimate <= 0)
            {
                // This case is usually covered by the initial unread count check or if the first page is empty.
            }
            else if (pageNumber == 1)
            {
                Console.WriteLine("[Processing] No unread threads found on the first page.");
            }
            else
            {
                Console.WriteLine("[Processing] No more unread threads found on subsequent pages.");
            }
        }

        /// <summary>
        /// Logs the final summary of operations.
        /// </summary>
        private void LogFinalSummary()
        {
            Console.WriteLine($"\n--- Final Summary ---");
            if (_totalUnreadThreadsEstimate >= 0)
            {
                Console.WriteLine($"Initial estimate of unread threads: {_totalUnreadThreadsEstimate}");
            }
            else
            {
                Console.WriteLine("Initial estimate of unread threads: Could not be determined.");
            }
            Console.WriteLine($"Total threads successfully marked as read: {_overallThreadsMarkedSuccessfullyCounter}");

            if (_overallThreadsMarkedSuccessfullyCounter == 0 && (_totalUnreadThreadsEstimate == 0 || _totalUnreadThreadsEstimate == -1))
            {
                Console.WriteLine("No unread threads were found or processed.");
            }
        }
    }
}