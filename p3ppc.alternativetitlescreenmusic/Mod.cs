using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using p3ppc.alternativetitlescreenmusic.Configuration;
using p3ppc.alternativetitlescreenmusic.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces;

namespace p3ppc.alternativetitlescreenmusic
{
    public unsafe class Mod : ModBase
    {
        private delegate long BgmPlayDelegate(ushort bgmId, ulong unused, byte delta, IntPtr adxName); // adx name is speculation, i've seen mods that are able to load whole files that aren't numbered
        private delegate void TitleScreenDelegate(IntPtr taskPtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] // necessary for some reason
        private delegate TitleTaskArgs* GetTaskArgsDelegate(IntPtr task);

        [StructLayout(LayoutKind.Explicit)] // also necessary for some reason
        private struct TitleTaskArgs
        {
            [FieldOffset(0x00)] public int Step;
            [FieldOffset(0x04)] public int Flags;
            [FieldOffset(0x08)] public float Timer;
            [FieldOffset(0x0C)] public int UnkC;
            [FieldOffset(0x10)] public int Selection;
            [FieldOffset(0x14)] public int InputMode;
            [FieldOffset(0x18)] public int Toggle;
            [FieldOffset(0x1C)] public int SelectionTimer;
            [FieldOffset(0x20)] public int Result;
            [FieldOffset(0x24)] public int SomeFlag;
            [FieldOffset(0x28)] public int Arg10;
            [FieldOffset(0x2C)] public int Arg11;
            [FieldOffset(0x30)] public int Arg12;
            [FieldOffset(0x50)] public IntPtr FilePtr; // only essential ones i can verify are these two
            [FieldOffset(0x58)] public IntPtr SpritePtr;
        }

        private readonly IModLoader _modLoader;
        private readonly Reloaded.Hooks.Definitions.IReloadedHooks _hooks;
        private readonly ILogger _logger;
        private readonly IMod _owner;
        private Config _configuration;
        private readonly IModConfig _modConfig;

        private IHook<TitleScreenDelegate> _titleScreenHook;
        private BgmPlayDelegate _bgmPlay;
        private GetTaskArgsDelegate _getTaskArgs;

        private List<ushort> _bgmTracks = new List<ushort>();
        private int _bgmCount = -1;
        private readonly Random _random = new Random();
        private bool _hasPlayedCustomBgm = false;

        private delegate int CheckSpriteDelegate(IntPtr spritePtr);
        private CheckSpriteDelegate _checkSprite;

        private delegate void FileCleanupDelegate(IntPtr filePtr);
        private FileCleanupDelegate _fileCleanup;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            Utils.Initialise(_logger, _configuration, _modLoader);
            InitializeBgmTracks();

            //
            // No idea if I even need all of these, the only essential ones are the first three I believe, the other 2
            // were also called in the same case, maybe you can go without them? i'm not smart enough for that
            //

            //
            // GetGlobalAddress is used to find functions when they're called in code, not the function itself
            // credit to AnimatedSwine for including that in the Utils.cs, massive life saver
            //

            Utils.SigScan("E8 ?? ?? ?? ?? 83 78 ?? 00 74 ??", "Task::GetArgs", address =>
            {
                var funcAddress = Utils.GetGlobalAddress((nint)(address + 1));
                _getTaskArgs = _hooks.CreateWrapper<GetTaskArgsDelegate>((long)funcAddress, out _);
                _logger.WriteLine($"[BGM Randomizer] Found Task::GetArgs at 0x{funcAddress:X}");
            });

            Utils.SigScan("40 53 41 54 41 56 41 57", "TitleScreen", address =>
            {
                _titleScreenHook = _hooks.CreateHook<TitleScreenDelegate>(TitleScreenHandler, address).Activate();
                _logger.WriteLine($"[BGM Randomizer] Hooked TitleScreen at 0x{address:X}");
            });

            Utils.SigScan("E8 ?? ?? ?? ?? BA 0F 00 00 00 8D 4A ??", "BGM Play Thunk", address =>
            {
                var funcAddress = Utils.GetGlobalAddress((nint)(address + 1));
                _bgmPlay = _hooks.CreateWrapper<BgmPlayDelegate>((long)funcAddress, out _);
                _logger.WriteLine($"[BGM Randomizer] Found BGM Play function at 0x{funcAddress:X}");
            });

            Utils.SigScan("48 89 5C 24 ?? 57 48 83 EC 30 48 8B 05 ?? ?? ?? ?? 48 31 E0 48 89 44 24 ?? 48 89 CB 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 4B ??", "CheckSprite", address =>
            {
                _checkSprite = _hooks.CreateWrapper<CheckSpriteDelegate>(address, out _);
                _logger.WriteLine($"[BGM Randomizer] Found CheckSprite function at 0x{address:X}");
            });

            Utils.SigScan("40 53 48 83 EC 20 48 89 CB 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 DB 0F 84 ?? ?? ?? ?? 83 7B ?? 00", "File Cleanup", address =>
            {
                _fileCleanup = _hooks.CreateWrapper<FileCleanupDelegate>(address, out _);
                _logger.WriteLine($"[BGM Randomizer] Found File Cleanup function at 0x{address:X}");
            });
        }

        private void InitializeBgmTracks()
        {
            var tracks = new HashSet<ushort>(); // basically just makes a list

            if (_configuration.AlwaysIncludeOriginal)
                tracks.Add(115); // default og track

            if (_configuration.IncludeAlternativeTracks)
            {
                tracks.Add(79); // placeholder alternative titles
                tracks.Add(77); // placeholder alternative title
            }

            _bgmTracks = tracks.ToList();

            _logger.WriteLine($"[BGM Randomizer] Initialized with {_bgmTracks.Count} BGM tracks");
            foreach (var track in _bgmTracks)
                _logger.WriteLine($"[BGM Randomizer] Track ID: {track} (0x{track:X2})");
        }

        private ushort SelectBgm()
        {
            if (_bgmTracks.Count == 0)
                return 115; 

            if (_configuration.RandomizeBgm)
            {
                _bgmCount = _random.Next(0, _bgmTracks.Count);
            }
            else
            {
                _bgmCount = (_bgmCount + 1) % _bgmTracks.Count;
            }

            return _bgmTracks[_bgmCount];
        }

        private void TitleScreenHandler(IntPtr taskPtr)
        {
            if (taskPtr == IntPtr.Zero || _getTaskArgs == null)
            {
                _titleScreenHook.OriginalFunction(taskPtr);
                return; 
            }

            //
            // Fallback code in case something fails
            //

            TitleTaskArgs* args = _getTaskArgs(taskPtr);
            if ((nint)args < 0x10000 || args == null)
            {
                _titleScreenHook.OriginalFunction(taskPtr);
                return;
            }

            int currentStep = args->Step;

            if (currentStep == 3)
            {
                IntPtr spritePtr = args->SpritePtr;
                if (spritePtr == IntPtr.Zero || _checkSprite == null || _checkSprite(spritePtr) == 0)
                {
                    _titleScreenHook.OriginalFunction(taskPtr);
                    return;
                }

                args->Step = 4;

                if (!_hasPlayedCustomBgm)
                {
                    ushort selectedBgm = SelectBgm();
                    _logger.WriteLine($"[BGM Randomizer] Playing custom BGM {selectedBgm} (0x{selectedBgm:X2})");

                    _bgmPlay?.Invoke(selectedBgm, 0, 0, IntPtr.Zero);
                    _hasPlayedCustomBgm = true;
                }

                IntPtr filePtr = args->FilePtr;
                if (filePtr != IntPtr.Zero && _fileCleanup != null)
                {
                    _fileCleanup(filePtr);
                    args->FilePtr = IntPtr.Zero;
                }
            }

            if (currentStep < 3 || currentStep > 7)
            {
                _hasPlayedCustomBgm = false;
            }

            _titleScreenHook.OriginalFunction(taskPtr); // run rest of code when done
        }

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Reinitializing BGM tracks");
            InitializeBgmTracks();
            _hasPlayedCustomBgm = false;
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}