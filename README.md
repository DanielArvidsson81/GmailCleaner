# Gmail Unread Thread Cleaner

A C# console application that uses the Gmail API to loop through your unread conversations (threads) in Gmail and mark them as read. This can be useful for quickly cleaning up a cluttered inbox where many unread items no longer require attention.

## Features

*   Fetches an initial estimate of total unread threads.
*   Processes unread threads in pages.
*   Marks threads as read by removing the "UNREAD" label.
*   Utilizes parallel processing for efficiency:
    *   Pipelined page fetching (fetching next page while processing current).
    *   Concurrent marking of threads within each page.
*   Configurable concurrency levels.
*   Secure OAuth 2.0 authentication.
*   User credentials (tokens) are stored securely in the user's AppData folder.

## Prerequisites

1.  **.NET SDK:** Version 6.0 or later (due to top-level statements and C# 10 features).
2.  **Google Cloud Project:**
    *   Go to the [Google Cloud Console](https://console.cloud.google.com/).
    *   Create a new project or select an existing one.
3.  **Enable Gmail API:**
    *   In your project, go to "APIs & Services" -> "Library".
    *   Search for "Gmail API" and enable it.
4.  **Create OAuth 2.0 Credentials:**
    *   Go to "APIs & Services" -> "Credentials".
    *   Click "+ CREATE CREDENTIALS" -> "OAuth client ID".
    *   **Configure the "OAuth consent screen"** if you haven't already:
        *   **User Type:** "External" (unless you have a G Suite account and want internal only).
        *   **App name:** Something like "Gmail Unread Cleaner".
        *   **User support email:** Your email.
        *   **Developer contact information:** Your email.
        *   Save and Continue.
        *   **Scopes:** Click "ADD OR REMOVE SCOPES". Search for "Gmail API" and select the scope ending in `.../auth/gmail.modify`. Click "Update".
        *   Save and Continue.
        *   **Test users:** Add your own Gmail address (the one whose inbox you want to clean).
        *   Save and Continue. Then "Back to Dashboard".
    *   Now, back on the "Credentials" page, click "+ CREATE CREDENTIALS" -> "OAuth client ID" again.
    *   **Application type:** "Desktop app".
    *   **Name:** "Gmail Cleaner Desktop Client" (or similar).
    *   Click "CREATE".
    *   A dialog will show your "Client ID" and "Client secret". Click "**DOWNLOAD JSON**".
    *   Rename this downloaded file to `client_secret.json`.

## Setup & Installation

1.  **Clone the Repository (or download the source code):**
    ```bash
    git clone <repository_url>
    cd GmailCleaner
    ```

2.  **Place `client_secret.json`:**
    *   Copy the `client_secret.json` file (downloaded in the Prerequisites step) into the root directory of the `GmailCleaner` project (the same directory as `GmailCleaner.csproj`).

3.  **Restore NuGet Packages:**
    *   Open a terminal or command prompt in the project root directory.
    *   Run:
        ```bash
        dotnet restore
        ```

4.  **Build the Project:**
    ```bash
    dotnet build --configuration Release
    ```
    (You can also use `Debug` configuration for development.)

## Usage

1.  **Run the Application:**
    *   Navigate to the output directory (e.g., `bin/Release/netX.X/`).
    *   Execute the application:
        ```bash
        dotnet GmailCleaner.dll
        ```
        Or, if you built an executable:
        ```bash
        ./GmailCleaner  # On Linux/macOS
        GmailCleaner.exe # On Windows
        ```

2.  **First-Time Authorization:**
    *   The first time you run the application, a browser window will open.
    *   You will be prompted to log in to your Google account and grant the application permission to "Read, compose, send, and permanently delete all your email from Gmail" (this is what the `gmail.modify` scope allows, though this app only reads and modifies labels).
    *   **Carefully review the permissions before granting access.**
    *   Once authorized, a token file (e.g., `gmail-dotnet-token.json`) will be created in your user's AppData folder (e.g., `C:\Users\YourUser\AppData\Roaming\GmailThreadCleaner\`). You won't need to re-authorize on subsequent runs unless you revoke access or delete this token file.

3.  **Operation:**
    *   The application will display its progress in the console, including:
        *   Fetching the total unread thread count.
        *   Processing threads page by page.
        *   Indicating which threads are being marked as read.
    *   Once complete, it will show a final summary.

## Configuration

The following settings can be adjusted in `Configuration/GmailConfiguration.cs`:

*   `MaxConcurrentItemOperations`: Maximum number of threads to mark as read concurrently *within* a single page's processing. (Default: 10)
*   `MaxConcurrentPageProcessingTasks`: Maximum number of pages to have their items processed concurrently. (Default: 3)
*   `MaxResultsPerPage`: Number of threads to fetch in a single API call when listing pages. (Default: 100)

Modify these values and rebuild the project if you need to fine-tune performance or API usage.

## Important Notes

*   **Irreversible Action:** Marking threads as read is an action that changes their state in Gmail. While not deleting data, ensure you are comfortable with this before running the script on your primary inbox.
*   **API Quotas:** Google Gmail API has usage quotas. For typical personal use, this script should operate well within limits. If you have an exceptionally large number of unread emails and run it very frequently, you might encounter rate limits. The built-in concurrency controls help mitigate this.
*   **Security:**
    *   The `client_secret.json` file identifies your application to Google. Do not share it publicly or commit it to public repositories if this were a shared project. For personal use, keeping it with the project is fine.
    *   The `token.json` (user credential token) stored in AppData grants access to your Gmail account. Protect this file as you would any sensitive credential.
*   **Error Handling:** The application includes error handling for common API issues and file operations. If you encounter persistent problems, check the console output for error messages.

## Troubleshooting

*   **`client_secret.json` not found:** Ensure the file is correctly named and placed in the project's root output directory (e.g., alongside the `.dll` or `.exe` after building) OR in the project source root before building if your build process copies it. The current setup expects it in the output directory.
*   **Authorization Errors:**
    *   Ensure you've enabled the Gmail API in your Google Cloud Project.
    *   Verify that the OAuth consent screen is configured correctly and your email is listed as a test user (if the app is in "testing" mode).
    *   Try deleting the `gmail-dotnet-token.json` file from your AppData folder (path shown in console output) and re-run to force re-authorization.
*   **API Rate Limits:** If you see errors related to quotas or rate limits, try reducing the `MaxConcurrentItemOperations` and `MaxConcurrentPageProcessingTasks` values in `GmailConfiguration.cs` and rebuild.

## Contributing

This is a personal utility, but if you have suggestions or improvements, feel free to fork the project and submit a pull request or open an issue.