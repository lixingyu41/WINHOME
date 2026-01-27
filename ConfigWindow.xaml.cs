using System.Windows;

namespace WINHOME
{
    public partial class ConfigWindow : Window
    {
        public ConfigWindow()
        {
            InitializeComponent();
            // keep same owner/position handled by caller
            Deactivated += ConfigWindow_Deactivated;
        }

        private void ConfigWindow_Deactivated(object? sender, EventArgs e)
        {
            try { this.Close(); } catch { }
        }
    }
}
