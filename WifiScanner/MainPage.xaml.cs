using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.WiFi;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WifiScanner
{

    public enum ChannelBand
    {
        /// <summary>
        /// 900 MHz band.
        /// </summary>
        [Description("900 MHz")]
        Band900MHz,

        /// <summary>
        /// 2.4 GHz band.
        /// </summary>
        [Description("2.4 GHz")]
        Band2_4GHz,

        /// <summary>
        /// 3.65 GHz band.
        /// </summary>
        [Description("3.65 GHz")]
        Band3_65GHz,

        /// <summary>
        /// 5.0 and 5.8 GHz bands.
        /// </summary>
        [Description("5 GHz")]
        Band5GHz,
    }
    public class NetworkGroup : List<Network>
    {
        public string Name { get; set; }
        public ChannelBand Band { get; set; }
    }

    public class Channel
    {
        public ChannelBand Band;
        public int Index;

        /// <summary>
        /// Get a channel based on its central frequency.
        /// </summary>
        /// <param name="centralFrequencyKHz">Central frequency of the WLAN channel, in kHz.</param>
        /// <returns>The identified channel, or <c>null</c> if unknown.</returns>
        /// <seealso href="https://en.wikipedia.org/wiki/List_of_WLAN_channels"/>
        public static Channel FromCentralFrequency(int centralFrequencyKHz)
        {
            // 900 MHz
            if (centralFrequencyKHz < 1000)
            {
                return new Channel { Band = ChannelBand.Band900MHz, Index = -1 };
            }

            // 2.4 GHz
            if ((centralFrequencyKHz >= 2412) && (centralFrequencyKHz <= 2472))
            {
                var index = 1 + (centralFrequencyKHz - 2412) / 5;
                return new Channel { Band = ChannelBand.Band2_4GHz, Index = index };
            }
            if (centralFrequencyKHz == 2484)
            {
                return new Channel { Band = ChannelBand.Band2_4GHz, Index = 14 };
            }

            // 5 GHz
            if ((centralFrequencyKHz >= 5035) && (centralFrequencyKHz <= 5865))
            {
                var index = (centralFrequencyKHz - 5000) / 5;
                return new Channel { Band = ChannelBand.Band5GHz, Index = index };
            }
            if ((centralFrequencyKHz >= 4915) && (centralFrequencyKHz <= 4980))
            {
                var index = (centralFrequencyKHz - 4000) / 5;
                return new Channel { Band = ChannelBand.Band5GHz, Index = index };
            }

            return null;
        }
    }

    public class Network
    {
        public WiFiAvailableNetwork WiFiNetwork;
        public Channel Channel;
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public ObservableCollection<WiFiAdapter> AvailableDevices { get; private set; }
            = new ObservableCollection<WiFiAdapter>();

        public WiFiAdapter SelectedDevice
        {
            get
            {
                var adapterIndex = DevicesComboBox.SelectedIndex;
                if ((adapterIndex < 0) || (adapterIndex >= AvailableDevices.Count))
                {
                    return null;
                }
                return AvailableDevices[adapterIndex];
            }
        }

        public ObservableCollection<NetworkGroup> AvailableNetworks { get; private set; }
            = new ObservableCollection<NetworkGroup>();


        public MainPage()
        {
            this.InitializeComponent();

            //NetworksList.ItemsSource = AvailableNetworks;
            //CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(NetworksList.ItemsSource);
            //PropertyGroupDescription groupDescription = new PropertyGroupDescription("Sex");
            //view.GroupDescriptions.Add(groupDescription);

            this.Loaded += MainPage_Loaded;
        }

        public static Symbol SymbolFromBars(byte numBars)
        {
            switch (numBars)
            {
            case 0:
                return Symbol.ZeroBars;
            case 1:
                return Symbol.OneBar;
            case 2:
                return Symbol.TwoBars;
            case 3:
                return Symbol.ThreeBars;
            case 4:
                return Symbol.FourBars;
            default:
                return Symbol.Help;
            }
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            WiFiAccessStatus accessInfo = await WiFiAdapter.RequestAccessAsync();
            StatusText.Text = $"Access: {accessInfo}";
            if (accessInfo != WiFiAccessStatus.Allowed)
            {
                return;
            }

            DevicesComboBox.SelectionChanged += DevicesComboBox_SelectionChanged;

            IReadOnlyList<WiFiAdapter> adapters = await WiFiAdapter.FindAllAdaptersAsync();
            StatusText.Text = $"Found {adapters.Count} WiFi adapters.";
            AvailableDevices.Clear();
            foreach (var adapter in adapters)
            {
                AvailableDevices.Add(adapter);
            }
            if (adapters.Count > 0)
            {
                DevicesComboBox.SelectedIndex = 0;
            }
        }

        private async void DevicesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var adapter = SelectedDevice;
            if (adapter == null)
            {
                return;
            }

            adapter.AvailableNetworksChanged += (WiFiAdapter ad, object args) => Adapter_AvailableNetworksChanged(Dispatcher, ad, args);
            StatusText.Text = $"Scanning WiFi networks with device {adapter.NetworkAdapter.NetworkAdapterId}...";
            await adapter.ScanAsync();
            StatusText.Text = $"Finished scan for device {adapter.NetworkAdapter.NetworkAdapterId}";
        }

        private void Adapter_AvailableNetworksChanged(CoreDispatcher dispatcher, WiFiAdapter adapter, object args)
        {
            var availableNetworks = new List<WiFiAvailableNetwork>(adapter.NetworkReport.AvailableNetworks);

            // Sort by channel frequency, then by SSID
            availableNetworks.Sort((WiFiAvailableNetwork n1, WiFiAvailableNetwork n2) => {
                if (n1.ChannelCenterFrequencyInKilohertz != n2.ChannelCenterFrequencyInKilohertz)
                {
                    return n1.ChannelCenterFrequencyInKilohertz - n2.ChannelCenterFrequencyInKilohertz;
                }
                return n1.Ssid.CompareTo(n2.Ssid);
            });

            NetworkGroup currentGroup = null;
            var networkGroups = new List<NetworkGroup>();
            foreach (var network in availableNetworks)
            {
                Channel channel = Channel.FromCentralFrequency(network.ChannelCenterFrequencyInKilohertz / 1000);
                if ((currentGroup == null) || (currentGroup.Band != channel.Band))
                {
                    currentGroup = new NetworkGroup
                    {
                        Name = $"{channel.Band}",
                        Band = channel.Band
                    };
                    networkGroups.Add(currentGroup);
                }
                currentGroup.Add(new Network
                {
                    WiFiNetwork = network,
                    Channel = channel
                });
            }

            _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => SetAvailableNetworks(networkGroups));
        }

        private void SetAvailableNetworks(List<NetworkGroup> networkGroups)
        {
            AvailableNetworks.Clear();
            foreach (var group in networkGroups)
            {
                AvailableNetworks.Add(group);
            }
        }
    }
}
