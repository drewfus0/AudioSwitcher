namespace AudioSwitcher.CoreAudio
{
    public enum AudioCategory
    {
        OutputSound = 0,
        OutputCommunications = 1,
        InputSound = 2,
        InputCommunications = 3
    }

    public static class AudioCategoryExtensions
    {
        public static string GetDisplayName(this AudioCategory category) => category switch
        {
            AudioCategory.OutputSound => "Output (Sound / Default)",
            AudioCategory.OutputCommunications => "Output (Communications)",
            AudioCategory.InputSound => "Input (Microphone / Sound)",
            AudioCategory.InputCommunications => "Input (Communications Mic)",
            _ => category.ToString()
        };

        public static string GetShortName(this AudioCategory category) => category switch
        {
            AudioCategory.OutputSound => "Output Sound",
            AudioCategory.OutputCommunications => "Output Comms",
            AudioCategory.InputSound => "Input Sound",
            AudioCategory.InputCommunications => "Input Comms",
            _ => category.ToString()
        };

        public static EDataFlow GetDataFlow(this AudioCategory category) => category switch
        {
            AudioCategory.OutputSound or AudioCategory.OutputCommunications => EDataFlow.eRender,
            AudioCategory.InputSound or AudioCategory.InputCommunications => EDataFlow.eCapture,
            _ => EDataFlow.eAll
        };

        public static bool IsCommunications(this AudioCategory category) => category switch
        {
            AudioCategory.OutputCommunications or AudioCategory.InputCommunications => true,
            _ => false
        };
    }
}
