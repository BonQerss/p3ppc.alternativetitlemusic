using System.ComponentModel;
using p3ppc.alternativetitlescreenmusic.Template.Configuration;
using Reloaded.Mod.Interfaces.Structs;

namespace p3ppc.alternativetitlescreenmusic.Configuration
{
    public class Config : Configurable<Config>
    {
        /*
            User Properties:
                - Please put all of your configurable properties here.
    
            By default, configuration saves as "Config.json" in mod user config folder.    
            Need more config files/classes? See Configuration.cs
    
            Available Attributes:
            - Category
            - DisplayName
            - Description
            - DefaultValue

            // Technically Supported but not Useful
            - Browsable
            - Localizable

            The `DefaultValue` attribute is used as part of the `Reset` button in Reloaded-Launcher.
        */
        [DisplayName("Randomize BGM")]
        [Description("If true, randomly selects BGM tracks. If false, cycles through them in order.")]
        [DefaultValue(true)]
        public bool RandomizeBgm { get; set; } = true;

        [DisplayName("Include Alternative Tracks")]
        [Description("Include preset alternative BGM tracks like battle themes, exploration music, etc.")]
        [DefaultValue(true)]
        public bool IncludeAlternativeTracks { get; set; } = true;

        [DisplayName("Always Include Original")]
        [Description("Always include the original title screen music (0x73) in the rotation.")]
        [DefaultValue(true)]
        public bool AlwaysIncludeOriginal { get; set; } = true;

        [DisplayName("Debug Logging")]
        [Description("Enable detailed logging for debugging purposes.")]
        [DefaultValue(false)]
        public bool DebugLogging { get; set; } = false;
    }

    /// <summary>
    /// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
    /// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
    /// </summary>
    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
        // 
    }
}
