using Godot;
using GodotGame.Services;

namespace GodotGame;

public partial class DeviceAuthFlow : Control
{
	private volatile bool _polling;
	private CancellationTokenSource? _cts;
	private int _pollInterval = 5;
	private string? _verificationUriComplete;

	private Label _statusLabel = null!;
	private Label _urlLabel = null!;
	private Label _codeLabel = null!;
	private TextureRect _qrRect = null!;
	private Button _openBrowserButton = null!;

	public override async void _Ready()
	{
		_statusLabel = GetNode<Label>("%StatusLabel");
		_urlLabel = GetNode<Label>("%UrlLabel");
		_codeLabel = GetNode<Label>("%CodeLabel");
		_qrRect = GetNode<TextureRect>("%QrRect");
		_openBrowserButton = GetNode<Button>("%OpenBrowserButton");

		_openBrowserButton.Pressed += OnOpenBrowser;
		_openBrowserButton.Visible = false;

		GetNode<Button>("%BackButton").Pressed += OnBack;

		UpdateStatus("Requesting device code...");

		// 1. Request device authorization
		var deviceAuth = await OAuthService.RequestDeviceAuthorizationAsync();

		if (deviceAuth == null)
		{
			UpdateStatus("Error: Could not start device flow.\nIs IdentityServer running on https://localhost:5001?");
			return;
		}

		// 2. Display the user code and verification URL
		_urlLabel.Text = deviceAuth.VerificationUri ?? "https://localhost:5001/device";
		_codeLabel.Text = deviceAuth.UserCode ?? "------";
		_verificationUriComplete = deviceAuth.VerificationUriComplete
			?? $"{deviceAuth.VerificationUri}?userCode={deviceAuth.UserCode}";
		_openBrowserButton.Visible = true;

		// 3. Generate and display QR code for the verification URL
		var qrTexture = QrCodeService.GenerateQrTexture(_verificationUriComplete, pixelsPerModule: 8);
		if (qrTexture != null)
		{
			_qrRect.Texture = qrTexture;
		}

		UpdateStatus("Scan the QR code or enter the code, then log in.");

		// 4. Start polling in the background
		_pollInterval = deviceAuth.Interval > 0 ? deviceAuth.Interval : 5;
		_polling = true;
		_cts = new CancellationTokenSource();

		var deviceCode = deviceAuth.DeviceCode!;
		_ = Task.Run(() => PollForTokenAsync(deviceCode, _cts.Token), _cts.Token);
	}

	private async Task PollForTokenAsync(string deviceCode, CancellationToken ct)
	{
		while (_polling && !ct.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(_pollInterval), ct).ConfigureAwait(false);

			if (ct.IsCancellationRequested) return;

			var result = await OAuthService.PollForDeviceTokenAsync(deviceCode).ConfigureAwait(false);

			switch (result)
			{
				case DevicePollResult.Success:
					_polling = false;
					// Fetch full user profile before navigating
					await OAuthService.FetchUserInfoAsync().ConfigureAwait(false);
					CallDeferred(MethodName.NavigateToAuthenticated);
					return;

				case DevicePollResult.Pending:
					CallDeferred(MethodName.UpdateStatus, "Waiting for authorization...");
					break;

				case DevicePollResult.SlowDown:
					_pollInterval += 5;
					CallDeferred(MethodName.UpdateStatus, $"Waiting... (polling every {_pollInterval}s)");
					break;

				case DevicePollResult.Error:
					_polling = false;
					CallDeferred(MethodName.ShowPollError);
					return;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}

	private void NavigateToAuthenticated()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Authenticated.tscn");
	}

	private void ShowPollError()
	{
		UpdateStatus("Error: Authorization failed or code expired.\nPress Back and try again.");
		_openBrowserButton.Visible = false;
	}

	private void OnOpenBrowser()
	{
		var url = _verificationUriComplete ?? _urlLabel.Text;
		if (!string.IsNullOrEmpty(url))
			OS.ShellOpen(url);
	}

	private void OnBack()
	{
		StopPolling();
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}

	private void UpdateStatus(string message)
	{
		_statusLabel.Text = message;
	}

	private void StopPolling()
	{
		_polling = false;
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;
	}

	public override void _ExitTree()
	{
		StopPolling();
	}
}
