using GmailCleaner.Configuration;
using GmailCleaner.Services;

Console.WriteLine($"Gmail Unread Thread Cleaner - Version {typeof(Program).Assembly.GetName().Version}");
Console.WriteLine("===================================================================");
Console.WriteLine($"Item-level concurrency: {GmailConfiguration.MaxConcurrentItemOperations}");
Console.WriteLine($"Page-level processing concurrency: {GmailConfiguration.MaxConcurrentPageProcessingTasks}");

var gmailServiceInstance = await GmailApiService.CreateAsync();
if (gmailServiceInstance == null)
{
    Console.WriteLine("\nFailed to initialize Gmail service. Exiting.");
}
else
{
    await gmailServiceInstance.MarkAllUnreadThreadsAsReadAsync();
}

Console.WriteLine("\nProcessing complete. Press any key to exit.");
Console.ReadKey();