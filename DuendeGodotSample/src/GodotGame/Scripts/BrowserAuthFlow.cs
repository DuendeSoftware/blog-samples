using System.Net;
using Godot;
using GodotGame.Services;
using Duende.IdentityModel;

namespace GodotGame;

public partial class BrowserAuthFlow : Control
{
	private HttpListener? _listener;
	private string? _codeVerifier;
	private bool _waitingForCallback;

	private Label _statusLabel = null!;
	private Button _startButton = null!;

	public override void _Ready()
	{
		_statusLabel = GetNode<Label>("%StatusLabel");
		_startButton = GetNode<Button>("%StartButton");

		_startButton.Pressed += OnStartLogin;
		GetNode<Button>("%BackButton").Pressed += OnBack;
	}

	private async void OnStartLogin()
	{
		_startButton.Disabled = true;
		UpdateStatus("Generating PKCE challenge...");

		// 1. Generate PKCE verifier + challenge
		var (verifier, challenge) = OAuthService.GeneratePkce();
		_codeVerifier = verifier;

		// 2. Start the local HttpListener to capture the callback
		_listener = new HttpListener();
		_listener.Prefixes.Add("http://localhost:8948/");
		_listener.Start();

		// 3. Build the authorization URL and open the system browser
		var state = CryptoRandom.CreateUniqueId(16);
		var authorizeUrl = await OAuthService.BuildAuthorizeUrl(challenge, state);

		if (authorizeUrl == null)
		{
			UpdateStatus("Error: Could not build authorize URL. Is IdentityServer running?");
			CleanupListener();
			_startButton.Disabled = false;
			return;
		}

		OS.ShellOpen(authorizeUrl);
		UpdateStatus("Waiting for browser callback...\n(Complete login in your browser)");

		// 4. Await the callback asynchronously — will not block the Godot main thread
		_waitingForCallback = true;
		HttpListenerContext context;
		try
		{
			context = await _listener.GetContextAsync();
		}
		catch (Exception ex)
		{
			UpdateStatus($"Cancelled or error: {ex.Message}");
			CleanupListener();
			_startButton.Disabled = false;
			_waitingForCallback = false;
			return;
		}

		_waitingForCallback = false;

		// 5. Extract authorization code and state from query string
		var query = context.Request.QueryString;
		var code = query["code"];
		var returnedState = query["state"];
		var error = query["error"];

		// 6. Serve a "close this tab" response to the browser
		// lang=html
		const string html =
            @"
            <!DOCTYPE html>
                  <html>
                  <head><title>Authentication Complete</title>
                  <style>
                    body { font-family: 'Inter', sans-serif; background: #2a2929; color: #fff;
                           display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
                    .card { background: #3a3939; padding: 40px; border-radius: 12px; text-align: center; }
                    h1 { color: #61fb92; margin-bottom: 12px; }
                    p { color: #ccc; }
                  </style>
                  </head>
                  <body>
				<div class=""card"">
                      <h1>Authentication Complete!</h1>
                      <p>You can close this tab and return to the game.</p>
                    </div>
                  </body>
				  </html>";

		var buffer = System.Text.Encoding.UTF8.GetBytes(html);
		context.Response.ContentType = "text/html";
		context.Response.ContentLength64 = buffer.Length;
		await context.Response.OutputStream.WriteAsync(buffer);
		context.Response.Close();

		// 7. Shut down listener
		CleanupListener();

		// 8. Handle error response from IdentityServer
		if (!string.IsNullOrEmpty(error))
		{
			UpdateStatus($"Login error: {error}");
			_startButton.Disabled = false;
			return;
		}

		// 9. Validate state to prevent CSRF
		if (returnedState != state)
		{
			UpdateStatus("Security error: State mismatch!");
			_startButton.Disabled = false;
			return;
		}

		if (string.IsNullOrEmpty(code))
		{
			UpdateStatus("Error: No authorization code received.");
			_startButton.Disabled = false;
			return;
		}

		// 10. Exchange authorization code for tokens
		UpdateStatus("Exchanging code for tokens...");
		var success = await OAuthService.ExchangeCodeForTokensAsync(code, _codeVerifier!);

		if (success)
		{
			// 11. Fetch full user profile from UserInfo endpoint
			UpdateStatus("Fetching user profile...");
			await OAuthService.FetchUserInfoAsync();
			GetTree().ChangeSceneToFile("res://Scenes/Authenticated.tscn");
		}
		else
		{
			UpdateStatus("Error: Token exchange failed. Check the console for details.");
			_startButton.Disabled = false;
		}
	}

	private void OnBack()
	{
		CleanupListener();
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}

	private void UpdateStatus(string message)
	{
		_statusLabel.Text = message;
	}

	private void CleanupListener()
	{
		if (_listener?.IsListening == true)
		{
			_listener.Stop();
			_listener.Close();
		}

		_listener = null;
	}

	public override void _ExitTree()
	{
		// Ensure the listener is released if the scene is destroyed mid-flow
		_waitingForCallback = false;
		CleanupListener();
	}
}
