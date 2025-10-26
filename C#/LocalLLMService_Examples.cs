using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using LlamaCpp.Service;

namespace LlamaCpp.Examples
{
    #region Example 1: Simple Console Application
    public class ConsoleExample
    {
        public static async Task Main()
        {
            Console.WriteLine("=== LocalLLMService Console Example ===\n");

            using var llm = new LocalLLMService(new LLMServiceConfig
            {
                ChatbotPath = "./build/chatbot",
                DefaultSystemPrompt = "You are my best assistance."
            });

            // Initialize
            Console.WriteLine("Initializing LLM...");
            await llm.InitializeAsync();
            Console.WriteLine("Ready!\n");

            // Example 1: Normal mode (full response)
            Console.WriteLine("--- Normal Mode ---");
            string response1 = await llm.GetResponseAsync("What is C++?");
            Console.WriteLine($"Response: {response1}\n");

            // Example 2: Streaming mode with events
            Console.WriteLine("--- Streaming Mode ---");
            llm.OnStreamUpdate += (sender, e) =>
            {
                Console.Write(e.Text.Substring(Math.Max(0, e.Text.Length - 1)));
                if (e.IsComplete)
                    Console.WriteLine($"\n[{e.TokensGenerated} tokens]");
            };

            await llm.GetResponseStreamingAsync("Explain pointers");

            Console.WriteLine("\nDone!");
        }
    }
    #endregion

    #region Example 2: WinForms Application
    public class WinFormsExample : Form
    {
        private LocalLLMService _llm;
        private TextBox _promptTextBox;
        private TextBox _responseTextBox;
        private Button _sendButton;
        private Button _sendStreamButton;
        private CheckBox _streamCheckBox;

        public WinFormsExample()
        {
            InitializeUI();
            InitializeLLM();
        }

        private void InitializeUI()
        {
            this.Text = "Local LLM Chat";
            this.Width = 600;
            this.Height = 500;

            // Prompt input
            var promptLabel = new Label { Text = "Your Question:", Top = 10, Left = 10, Width = 100 };
            _promptTextBox = new TextBox { Top = 10, Left = 120, Width = 450, Height = 80, Multiline = true };

            // Response output
            var responseLabel = new Label { Text = "Response:", Top = 100, Left = 10, Width = 100 };
            _responseTextBox = new TextBox
            {
                Top = 100,
                Left = 120,
                Width = 450,
                Height = 250,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true
            };

            // Buttons
            _sendButton = new Button { Text = "Send (Normal)", Top = 360, Left = 120, Width = 120 };
            _sendButton.Click += SendButton_Click;

            _sendStreamButton = new Button { Text = "Send (Streaming)", Top = 360, Left = 250, Width = 120 };
            _sendStreamButton.Click += SendStreamButton_Click;

            _streamCheckBox = new CheckBox { Text = "Auto-stream", Top = 360, Left = 380, Width = 100, Checked = true };

            // Add controls
            this.Controls.AddRange(new Control[]
            {
                promptLabel, _promptTextBox,
                responseLabel, _responseTextBox,
                _sendButton, _sendStreamButton, _streamCheckBox
            });
        }

        private async void InitializeLLM()
        {
            _llm = new LocalLLMService();

            try
            {
                _responseTextBox.Text = "Initializing LLM...";
                await _llm.InitializeAsync();
                _responseTextBox.Text = "Ready! Ask me anything.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize: {ex.Message}", "Error");
            }
        }

        // Normal mode
        private async void SendButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_promptTextBox.Text))
                return;

            _sendButton.Enabled = false;
            _sendStreamButton.Enabled = false;
            _responseTextBox.Text = "Thinking...";

            try
            {
                string response = await _llm.GetResponseAsync(_promptTextBox.Text);
                _responseTextBox.Text = response;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error");
            }
            finally
            {
                _sendButton.Enabled = true;
                _sendStreamButton.Enabled = true;
            }
        }

        // Streaming mode - Easy way!
        private async void SendStreamButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_promptTextBox.Text))
                return;

            _sendButton.Enabled = false;
            _sendStreamButton.Enabled = false;
            _responseTextBox.Text = "";

            try
            {
                // This single line handles everything!
                await _llm.GetResponseToTextBox(_responseTextBox, _promptTextBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error");
            }
            finally
            {
                _sendButton.Enabled = true;
                _sendStreamButton.Enabled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _llm?.Dispose();
            base.OnFormClosing(e);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new WinFormsExample());
        }
    }
    #endregion

    #region Example 3: WPF Application (XAML Code Behind)
    // XAML file would contain:
    /*
    <Window x:Class="LlamaCpp.Examples.WPFExample"
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            Title="Local LLM Chat" Height="500" Width="600">
        <Grid Margin="10">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="100"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <Label Grid.Row="0" Content="Your Question:"/>
            <TextBox Grid.Row="1" Name="PromptTextBox" TextWrapping="Wrap" AcceptsReturn="True"/>

            <Label Grid.Row="2" Content="Response:" Margin="0,10,0,0"/>
            <TextBox Grid.Row="3" Name="ResponseTextBox" TextWrapping="Wrap" IsReadOnly="True"
                     VerticalScrollBarVisibility="Auto" Margin="0,5,0,0"/>

            <StackPanel Grid.Row="4" Orientation="Horizontal" Margin="0,10,0,0">
                <Button Name="SendButton" Content="Send (Normal)" Width="120" Margin="0,0,10,0" Click="SendButton_Click"/>
                <Button Name="SendStreamButton" Content="Send (Streaming)" Width="120" Margin="0,0,10,0" Click="SendStreamButton_Click"/>
                <CheckBox Name="StreamCheckBox" Content="Auto-stream" VerticalAlignment="Center" IsChecked="True"/>
            </StackPanel>
        </Grid>
    </Window>
    */

    public partial class WPFExample : System.Windows.Window
    {
        private LocalLLMService _llm;

        public WPFExample()
        {
            InitializeComponent();
            InitializeLLM();
        }

        private async void InitializeLLM()
        {
            _llm = new LocalLLMService();

            try
            {
                ResponseTextBox.Text = "Initializing LLM...";
                await _llm.InitializeAsync();
                ResponseTextBox.Text = "Ready! Ask me anything.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to initialize: {ex.Message}", "Error");
            }
        }

        // Normal mode
        private async void SendButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PromptTextBox.Text))
                return;

            SendButton.IsEnabled = false;
            SendStreamButton.IsEnabled = false;
            ResponseTextBox.Text = "Thinking...";

            try
            {
                string response = await _llm.GetResponseAsync(PromptTextBox.Text);
                ResponseTextBox.Text = response;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
            }
            finally
            {
                SendButton.IsEnabled = true;
                SendStreamButton.IsEnabled = true;
            }
        }

        // Streaming mode - Easy way!
        private async void SendStreamButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PromptTextBox.Text))
                return;

            SendButton.IsEnabled = false;
            SendStreamButton.IsEnabled = false;
            ResponseTextBox.Text = "";

            try
            {
                // This single line handles everything!
                await _llm.GetResponseToTextBoxWPF(ResponseTextBox, PromptTextBox.Text);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
            }
            finally
            {
                SendButton.IsEnabled = true;
                SendStreamButton.IsEnabled = true;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _llm?.Dispose();
            base.OnClosing(e);
        }
    }
    #endregion

    #region Example 4: Custom Event Handling
    public class CustomEventExample
    {
        public static async Task RunAsync()
        {
            using var llm = new LocalLLMService();
            await llm.InitializeAsync();

            // Subscribe with custom handler
            llm.OnStreamUpdate += (sender, e) =>
            {
                Console.WriteLine($"[Progress: {e.TokensGenerated} tokens]");

                // You can update ANY UI control here
                // myLabel.Text = $"Tokens: {e.TokensGenerated}";
                // myProgressBar.Value = e.TokensGenerated;
                // myRichTextBox.Text = e.Text;

                if (e.IsComplete)
                {
                    Console.WriteLine("Generation complete!");
                }
            };

            string response = await llm.GetResponseStreamingAsync("Tell me a story");
            Console.WriteLine($"\nFinal: {response}");
        }
    }
    #endregion

    #region Example 5: Multiple Requests
    public class MultipleRequestsExample
    {
        public static async Task RunAsync()
        {
            using var llm = new LocalLLMService();
            await llm.InitializeAsync();

            string[] questions = {
                "What is C++?",
                "Explain pointers",
                "What is RAII?"
            };

            foreach (var question in questions)
            {
                Console.WriteLine($"\nQ: {question}");

                // Normal mode for quick answers
                string answer = await llm.GetResponseAsync(question);
                Console.WriteLine($"A: {answer}");
            }
        }
    }
    #endregion
}

