using Godot;
using GodotGame.Services;

namespace GodotGame;

public partial class AuthenticatedScene : Control
{
    public override void _Ready()
    {
        var claims = TokenStorage.UserClaims;

        GetNode<Label>("%NameLabel").Text = $"Name: {claims.GetValueOrDefault("name", "N/A")}";
        GetNode<Label>("%SubLabel").Text = $"Subject: {claims.GetValueOrDefault("sub", "N/A")}";
        GetNode<Label>("%EmailLabel").Text = $"Email: {claims.GetValueOrDefault("email", "N/A")}";

        // Show the greeting with the user's name
        var name = claims.GetValueOrDefault("name", "User");
        GetNode<Label>("%GreetingLabel").Text = $"Welcome, {name}!";

        GetNode<Button>("%LogoutButton").Pressed += OnLogout;
    }

    private void OnLogout()
    {
        TokenStorage.Clear();
        GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
    }
}
