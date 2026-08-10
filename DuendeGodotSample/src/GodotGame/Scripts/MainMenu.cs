using Godot;

namespace GodotGame;

public partial class MainMenu : Control
{
	public override void _Ready()
	{
		GetNode<Button>("%BrowserAuthButton").Pressed += OnBrowserAuth;
		GetNode<Button>("%DeviceAuthButton").Pressed += OnDeviceAuth;
	}

	private void OnBrowserAuth()
	{
		GetTree().ChangeSceneToFile("res://Scenes/BrowserAuth.tscn");
	}

	private void OnDeviceAuth()
	{
		GetTree().ChangeSceneToFile("res://Scenes/DeviceAuth.tscn");
	}
}
