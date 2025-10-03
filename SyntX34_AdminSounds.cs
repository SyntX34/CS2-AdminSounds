using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json;
using MenuManager;
using CounterStrikeSharp.API.Core.Translations;

namespace SyntX34_AdminSounds;

[MinimumApiVersion(80)]
public class SyntX34_AdminSounds : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "Admin Sounds";
    public override string ModuleVersion => "1.1.4";
    public override string ModuleAuthor => "SyntX34";
    public override string ModuleDescription => "Admin sound plugin for CS2";

    public PluginConfig Config { get; set; } = new();
    public SoundsConfig SoundsConfig { get; set; } = new();
    
    private readonly Dictionary<int, DateTime> _playerCooldowns = new();
    private readonly Dictionary<int, bool> _playerStoppedSounds = new();
    private readonly Dictionary<int, bool> _playerBlockedSounds = new();
    private bool _isSoundPlaying = false;
    private DateTime _soundEndTime = DateTime.MinValue;
    private string _currentSoundName = ""; // Track current sound name
    private CounterStrikeSharp.API.Modules.Timers.Timer? _soundDurationTimer;

    // MenuManager capability
    private readonly PluginCapability<IMenuApi?> _menuCapability = new("menu:nfcore");
    private IMenuApi? _menuApi;

    public void OnConfigParsed(PluginConfig config)
    {
        Config = config;
        LoadSoundsConfig();
    }

    private void LoadSoundsConfig()
    {
        string soundsConfigPath = Path.Combine(ModuleDirectory, "sounds.json");
        Console.WriteLine($"[Admin Sounds DEBUG] Looking for sounds.json at: {soundsConfigPath}");
        
        if (!File.Exists(soundsConfigPath))
        {
            Console.WriteLine($"[Admin Sounds] sounds.json not found at {soundsConfigPath}. Creating empty file...");
            
            // Create empty sounds configuration
            var emptySoundsConfig = new SoundsConfig();

            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string emptyJson = JsonSerializer.Serialize(emptySoundsConfig, options);
                File.WriteAllText(soundsConfigPath, emptyJson);
                Console.WriteLine($"[Admin Sounds] Created empty sounds.json file");
                Console.WriteLine($"[Admin Sounds] Please edit sounds.json to add your sound configurations");
                
                SoundsConfig = emptySoundsConfig;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Admin Sounds] Error creating empty sounds.json: {ex.Message}");
                SoundsConfig = new SoundsConfig();
            }
            
            return;
        }

        try
        {
            string soundsJson = File.ReadAllText(soundsConfigPath);
            Console.WriteLine($"[Admin Sounds DEBUG] Raw JSON content: {soundsJson}");
            
            var deserializedConfig = JsonSerializer.Deserialize<SoundsConfig>(soundsJson);
            if (deserializedConfig == null)
            {
                Console.WriteLine("[Admin Sounds] Failed to deserialize sounds.json - null result");
                SoundsConfig = new SoundsConfig();
                return;
            }
            
            SoundsConfig = deserializedConfig;
            
            // Ensure Sounds dictionary is not null
            if (SoundsConfig.Sounds == null)
            {
                Console.WriteLine("[Admin Sounds] Sounds dictionary is null, initializing empty dictionary");
                SoundsConfig.Sounds = new Dictionary<string, SoundConfig>();
            }
            
            Console.WriteLine($"[Admin Sounds] Loaded {SoundsConfig.Sounds.Count} sounds from sounds.json");
            
            if (SoundsConfig.Sounds.Count == 0)
            {
                Console.WriteLine("[Admin Sounds] WARNING: No sounds found in sounds.json. Please add sound configurations.");
            }
            else
            {
                foreach (var sound in SoundsConfig.Sounds)
                {
                    Console.WriteLine($"[Admin Sounds DEBUG] Sound: {sound.Key} -> SoundEventName: {sound.Value.SoundEventName}, Duration: {sound.Value.Duration}, Cooldown: {sound.Value.Cooldown}");
                }
            }
        }
        catch (JsonException jsonEx)
        {
            Console.WriteLine($"[Admin Sounds] JSON parsing error in sounds.json: {jsonEx.Message}");
            Console.WriteLine($"[Admin Sounds] Please check your sounds.json file for syntax errors");
            SoundsConfig = new SoundsConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Admin Sounds] Error loading sounds.json: {ex.Message}");
            Console.WriteLine($"[Admin Sounds DEBUG] Exception details: {ex}");
            SoundsConfig = new SoundsConfig();
        }
    }

    public override void Load(bool hotReload)
    {
        if (!Config.PluginEnabled)
        {
            Console.WriteLine("[Admin Sounds] Plugin is disabled in config.");
            return;
        }

        Console.WriteLine("[Admin Sounds] Plugin loading...");

        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        
        AddTimer(1.0f, () =>
        {
            foreach (var sound in SoundsConfig.Sounds.Values)
            {
                if (!string.IsNullOrEmpty(sound.SoundEventName))
                {
                    Server.ExecuteCommand($"sv_soundemitter_filecheck 0");
                }
            }
            Console.WriteLine($"[Admin Sounds] Plugin loaded with {SoundsConfig.Sounds.Count} sounds");
            
            if (SoundsConfig.Sounds.Count == 0)
            {
                Console.WriteLine("[Admin Sounds] WARNING: No sounds configured! Please edit sounds.json to add your sound files.");
            }
        });

        RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
    }

    private void OnServerPrecacheResources(ResourceManifest manifest)
    {
        manifest.AddResource("soundevents/admin_sounds.vsndevts");
    }

    [ConsoleCommand("css_adminsounds", "Open admin sounds menu")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAdminSoundsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!Config.PluginEnabled)
        {
#pragma warning disable CS8602
            player.PrintToChat($" {ChatColors.DarkRed}[Admin Sounds]{ChatColors.Yellow} {Localizer["PluginDisabled"]}");
#pragma warning restore CS8602
            return;
        }

        if (player == null || !player.IsValid || player.IsBot)
            return;

        if (!string.IsNullOrEmpty(Config.AdminPermission))
        {
            if (!AdminManager.PlayerHasPermissions(player, Config.AdminPermission))
            {
                player.PrintToChat($" {ChatColors.Red}[Admin Sounds] {Localizer["NoPermission"]}");
                return;
            }
        }

        if (_isSoundPlaying && DateTime.Now < _soundEndTime)
        {
            player.PrintToChat($" {ChatColors.Yellow}[Admin Sounds] {Localizer["SoundAlreadyPlaying", _currentSoundName]}");
            return;
        }

        if (_playerCooldowns.ContainsKey(player.Slot) && DateTime.Now < _playerCooldowns[player.Slot])
        {
            var remainingTime = (_playerCooldowns[player.Slot] - DateTime.Now).TotalSeconds;
            player.PrintToChat($" {ChatColors.Yellow}[Admin Sounds] {Localizer["CooldownActive", remainingTime.ToString("F0")]}");
            return;
        }

        if (SoundsConfig.Sounds == null || SoundsConfig.Sounds.Count == 0)
        {
            player.PrintToChat($" {ChatColors.Red}[Admin Sounds] {Localizer["NoSoundsConfigured"]}");
            Console.WriteLine($"[Admin Sounds] Player {player.PlayerName} tried to use sounds but none are configured");
            return;
        }

        OpenSoundsMenu(player);
    }

    [ConsoleCommand("css_reloadsounds", "Reload sounds configuration")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnReloadSoundsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && !string.IsNullOrEmpty(Config.AdminPermission))
        {
            if (!AdminManager.PlayerHasPermissions(player, Config.AdminPermission))
            {
                player.PrintToChat($" {ChatColors.Red}[Admin Sounds] {Localizer["NoPermission"]}");
                return;
            }
        }

        LoadSoundsConfig();
        
        string message = Localizer["SoundsReloaded", SoundsConfig.Sounds.Count.ToString()];
        Console.WriteLine($"[Admin Sounds] {message}");
        
        if (player != null)
        {
            player.PrintToChat($" {ChatColors.Green}[Admin Sounds] {message}");
        }
    }

    [ConsoleCommand("css_stop", "Stop hearing the current sound")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStopCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!Config.PluginEnabled)
            return;

        if (player == null || !player.IsValid || player.IsBot)
            return;

        if (!_isSoundPlaying || DateTime.Now >= _soundEndTime)
        {
            player.PrintToChat($" {ChatColors.Yellow}[Admin Sounds] {Localizer["NoSoundPlaying"]}");
            return;
        }

        _playerStoppedSounds[player.Slot] = true;
        player.PrintToChat($" {ChatColors.Green}[Admin Sounds] {Localizer["SoundStopped"]}");
    }

    [ConsoleCommand("css_stopall", "Block/unblock all sounds from this plugin")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStopAllCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!Config.PluginEnabled)
            return;

        if (player == null || !player.IsValid || player.IsBot)
            return;

        bool currentlyBlocked = _playerBlockedSounds.ContainsKey(player.Slot) && _playerBlockedSounds[player.Slot];
        
        if (currentlyBlocked)
        {
            _playerBlockedSounds[player.Slot] = false;
            player.PrintToChat($" {ChatColors.Green}[Admin Sounds] {Localizer["SoundsUnblocked"]}");
        }
        else
        {
            _playerBlockedSounds[player.Slot] = true;
            player.PrintToChat($" {ChatColors.Red}[Admin Sounds] {Localizer["SoundsBlocked"]}");
        }
    }

    private void OpenSoundsMenu(CCSPlayerController player)
    {
        // Try to get MenuManager API when actually needed
        if (_menuApi == null)
        {
            try
            {
                _menuApi = _menuCapability.Get();
                Console.WriteLine("[Admin Sounds] MenuManager API successfully loaded on demand.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Admin Sounds] MenuManager API not available: {ex.Message}");
                player.PrintToChat($" {ChatColors.Red}[Admin Sounds] {Localizer["MenuSystemUnavailable"]}");
                return;
            }
        }

        Console.WriteLine($"[Admin Sounds] Opening center menu for player {player.PlayerName}");
        try
        {
#pragma warning disable CS8602
            var menu = _menuApi.GetMenu(Localizer["SelectSoundMenuTitle"]);
#pragma warning restore CS8602

            foreach (var sound in SoundsConfig.Sounds)
            {
                var soundKey = sound.Key;
                var soundValue = sound.Value;
                menu.AddMenuOption(soundKey, (controller, option) =>
                {
                    PlaySound(controller, soundKey, soundValue);
                });
            }

            menu.ExitButton = true;
            menu.Open(player);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Admin Sounds] Failed to open center menu for player {player.PlayerName}: {ex.Message}");
            player.PrintToChat($" {ChatColors.Red}[Admin Sounds] {Localizer["MenuError"]}");
        }
    }

    private void PlaySound(CCSPlayerController player, string soundName, SoundConfig soundConfig)
    {
        if (string.IsNullOrEmpty(soundConfig.SoundEventName))
        {
            player.PrintToChat($" {ChatColors.Red}[Admin Sounds] {Localizer["SoundEventNotConfigured"]}");
            return;
        }

        if (_isSoundPlaying && DateTime.Now < _soundEndTime)
        {
            player.PrintToChat($" {ChatColors.Yellow}[Admin Sounds] {Localizer["SoundAlreadyPlaying", _currentSoundName]}");
            return;
        }

        if (_playerCooldowns.ContainsKey(player.Slot) && DateTime.Now < _playerCooldowns[player.Slot])
        {
            var remainingTime = (_playerCooldowns[player.Slot] - DateTime.Now).TotalSeconds;
            player.PrintToChat($" {ChatColors.Yellow}[Admin Sounds] {Localizer["CooldownActive", remainingTime.ToString("F0")]}");
            return;
        }

        _isSoundPlaying = true;
        _soundEndTime = DateTime.Now.AddSeconds(soundConfig.Duration);
        _currentSoundName = soundName; // Store the current sound name

        _playerCooldowns[player.Slot] = DateTime.Now.AddSeconds(soundConfig.Duration + soundConfig.Cooldown);

        _playerStoppedSounds.Clear();

        var validPlayers = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToList();
        
        foreach (var targetPlayer in validPlayers)
        {
            if (_playerBlockedSounds.ContainsKey(targetPlayer.Slot) && _playerBlockedSounds[targetPlayer.Slot])
                continue;

            if (_playerStoppedSounds.ContainsKey(targetPlayer.Slot) && _playerStoppedSounds[targetPlayer.Slot])
                continue;

            // Use Utils.EmitSound instead of play command
            targetPlayer.EmitSound(soundConfig.SoundEventName);
        }

        Server.PrintToChatAll($" {ChatColors.Green}[Admin Sounds] {ChatColors.White}{player.PlayerName} {ChatColors.Green}{Localizer["PlayedSound", soundName]}");

        _soundDurationTimer?.Kill();
        _soundDurationTimer = AddTimer(soundConfig.Duration, () =>
        {
            _isSoundPlaying = false;
            _soundEndTime = DateTime.MinValue;
            _currentSoundName = ""; // Clear the current sound name
            _playerStoppedSounds.Clear();
        });
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // Reset cooldowns on round start
        // _playerCooldowns.Clear();
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            var slot = player.Slot;
            _playerCooldowns.Remove(slot);
            _playerStoppedSounds.Remove(slot);
            _playerBlockedSounds.Remove(slot);
        }
        
        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        _soundDurationTimer?.Kill();
        _playerCooldowns.Clear();
        _playerStoppedSounds.Clear();
        _playerBlockedSounds.Clear();
    }
}