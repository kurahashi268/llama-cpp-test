using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LLamaService.WinFormsSample
{
    /// <summary>
    /// Minimal Windows Forms sample that demonstrates how to drive the C++ backend
    /// via <see cref="LlamaClient"/>. The form lets you initialize the model,
    /// send prompts, and watch streaming updates in real-time.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox _txtExecutablePath;
        private readonly Button _btnInitialize;
        private readonly Button _btnShutdown;
        private readonly TextBox _txtSystemPrompt;
        private readonly TextBox _txtUserPrompt;
        private readonly Button _btnGenerate;
        private readonly Button _btnCancel;
        private readonly CheckBox _chkStreaming;
        private readonly CheckBox _chkDebug;
        private readonly TextBox _txtOutput;
        private readonly Label _lblStatus;

        private LlamaClient _client;
        private bool _isInitialized;
        private CancellationTokenSource _generationCts;

        public MainForm()
        {
            Text = "llama.cpp WinForms Sample";
            MinimumSize = new Size(900, 680);

            _txtExecutablePath = new TextBox
            {
                PlaceholderText = "Path to chatbot executable (e.g. C:\\llama\\llm.exe)",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "llm/llm.exe"
            };

            _btnInitialize = new Button
            {
                Text = "Initialize",
                AutoSize = true
            };
            _btnInitialize.Click += OnInitializeClicked;

            _btnShutdown = new Button
            {
                Text = "Shutdown",
                AutoSize = true,
                Enabled = false
            };
            _btnShutdown.Click += OnShutdownClicked;

            _chkDebug = new CheckBox
            {
                Text = "Enable debug output",
                AutoSize = true
            };

            _txtSystemPrompt = new TextBox
            {
                Multiline = true,
                Height = 80,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "You are a helpful medicine assistant."
            };

            _txtUserPrompt = new TextBox
            {
                Multiline = true,
                Height = 120,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                PlaceholderText = "Enter your user prompt here..."
            };

            _chkStreaming = new CheckBox
            {
                Text = "Stream tokens",
                AutoSize = true,
                Checked = true
            };

            _btnGenerate = new Button
            {
                Text = "Generate",
                AutoSize = true,
                Enabled = false
            };
            _btnGenerate.Click += OnGenerateClicked;

            _btnCancel = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                Enabled = false
            };
            _btnCancel.Click += OnCancelClicked;

            _txtOutput = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                Height = 280
            };

            _lblStatus = new Label
            {
                AutoSize = true,
                ForeColor = Color.DarkGreen,
                Text = "Status: idle"
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 10,
                Padding = new Padding(10),
                AutoScroll = true
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Executable path
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Buttons + debug checkbox
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // System prompt label
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // System prompt textbox
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // User prompt label
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // User prompt textbox
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // streaming + buttons
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Output label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Output textbox
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Status

            layout.Controls.Add(new Label { Text = "Chatbot executable:", AutoSize = true }, 0, 0);
            layout.SetColumnSpan(_txtExecutablePath, 4);
            layout.Controls.Add(_txtExecutablePath, 0, 1);

            var initPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Anchor = AnchorStyles.Left
            };
            initPanel.Controls.Add(_btnInitialize);
            initPanel.Controls.Add(_btnShutdown);
            initPanel.Controls.Add(_chkDebug);
            layout.Controls.Add(initPanel, 0, 2);
            layout.SetColumnSpan(initPanel, 4);

            layout.Controls.Add(new Label { Text = "System prompt:", AutoSize = true }, 0, 3);
            layout.SetColumnSpan(_txtSystemPrompt, 4);
            layout.Controls.Add(_txtSystemPrompt, 0, 4);

            layout.Controls.Add(new Label { Text = "User prompt:", AutoSize = true }, 0, 5);
            layout.SetColumnSpan(_txtUserPrompt, 4);
            layout.Controls.Add(_txtUserPrompt, 0, 6);

            var generationPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Anchor = AnchorStyles.Left
            };
            generationPanel.Controls.Add(_chkStreaming);
            generationPanel.Controls.Add(_btnGenerate);
            generationPanel.Controls.Add(_btnCancel);
            layout.Controls.Add(generationPanel, 0, 7);
            layout.SetColumnSpan(generationPanel, 4);

            layout.Controls.Add(new Label { Text = "Output:", AutoSize = true }, 0, 8);
            layout.SetColumnSpan(_txtOutput, 4);
            layout.Controls.Add(_txtOutput, 0, 9);

            layout.Controls.Add(_lblStatus, 0, 10);
            layout.SetColumnSpan(_lblStatus, 4);

            Controls.Add(layout);
        }

        private async void OnInitializeClicked(object sender, EventArgs e)
        {
            if (_isInitialized)
            {
                SetStatus("Already initialized.");
                return;
            }

            ToggleUiDuringInit(isBusy: true);

            try
            {
                var config = new LlamaClientConfig
                {
                    ChatbotPath = _txtExecutablePath.Text.Trim(),
                    DefaultSystemPrompt = _txtSystemPrompt.Text,
                    EnableDebugOutput = _chkDebug.Checked
                };

                var client = new LlamaClient(config);
                await Task.Run(client.Initialize);
                client.OnStreamUpdate += HandleStreamUpdate;

                _client = client;
                _isInitialized = true;

                SetStatus("Initialization complete.");
                _btnGenerate.Enabled = true;
                _btnShutdown.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Initialization failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Initialization failed.");
            }
            finally
            {
                ToggleUiDuringInit(isBusy: false);
            }
        }

        private async void OnShutdownClicked(object sender, EventArgs e)
        {
            await ShutdownClientAsync();
        }

        private async void OnGenerateClicked(object sender, EventArgs e)
        {
            if (!_isInitialized || _client == null)
            {
                MessageBox.Show(this, "Initialize the client first.", "Not initialized", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtUserPrompt.Text))
            {
                MessageBox.Show(this, "Enter a user prompt.", "Missing prompt", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _btnGenerate.Enabled = false;
            _btnCancel.Enabled = true;
            _txtOutput.Text = string.Empty;
            SetStatus("Generating...");

            _generationCts = new CancellationTokenSource();

            try
            {
                string systemPrompt = _txtSystemPrompt.Text;
                string userPrompt = _txtUserPrompt.Text;
                string result;

                if (_chkStreaming.Checked)
                {
                    result = await Task.Run(() =>
                        _client.GenerateStreamingAsync(
                            userPrompt,
                            HandleStreamUpdateCallback,
                            systemPrompt,
                            _generationCts.Token), _generationCts.Token);
                }
                else
                {
                    result = await Task.Run(() =>
                        _client.GenerateAsync(
                            userPrompt,
                            systemPrompt,
                            _generationCts.Token), _generationCts.Token);
                    UpdateOutputSafely(result);
                }

                SetStatus("Generation complete.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Generation cancelled.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Generation error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Generation failed.");
            }
            finally
            {
                _btnGenerate.Enabled = true;
                _btnCancel.Enabled = false;
                _generationCts?.Dispose();
                _generationCts = null;
            }
        }

        private void HandleStreamUpdate(object sender, StreamUpdateEventArgs e)
        {
            UpdateOutputSafely(e.Text);

            if (e.IsComplete)
            {
                SetStatus($"Streaming complete ({e.TokensGenerated} tokens)");
            }
            else
            {
                SetStatus($"Streaming... ({e.TokensGenerated} tokens)");
            }
        }

        private void HandleStreamUpdateCallback(string text, int tokensGenerated, bool isComplete)
        {
            UpdateOutputSafely(text);

            if (isComplete)
            {
                SetStatus($"Streaming complete ({tokensGenerated} tokens)");
            }
            else
            {
                SetStatus($"Streaming... ({tokensGenerated} tokens)");
            }
        }

        private void UpdateOutputSafely(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateOutputSafely), text);
                return;
            }

            _txtOutput.Text = text ?? string.Empty;
        }

        private void SetStatus(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), message);
                return;
            }

            _lblStatus.Text = $"Status: {message}";
        }

        private void ToggleUiDuringInit(bool isBusy)
        {
            _btnInitialize.Enabled = !isBusy;
            _txtExecutablePath.Enabled = !isBusy;
            _chkDebug.Enabled = !isBusy;
            SetStatus(isBusy ? "Initializing..." : "Idle");
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            if (_generationCts == null)
            {
                return;
            }

            if (!_generationCts.IsCancellationRequested)
            {
                _generationCts.Cancel();
                SetStatus("Cancelling generation...");
            }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_generationCts != null)
            {
                _generationCts.Cancel();
            }

            await ShutdownClientAsync();
            base.OnFormClosing(e);
        }

        private async Task ShutdownClientAsync()
        {
            if (_client == null)
            {
                return;
            }

            _btnGenerate.Enabled = false;
            _btnShutdown.Enabled = false;
            SetStatus("Shutting down...");

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        _client.Shutdown();
                    }
                    catch
                    {
                        // Ignore shutdown exceptions; process may already be gone.
                    }
                    finally
                    {
                        _client.Dispose();
                    }
                });
            }
            finally
            {
                _client = null;
                _isInitialized = false;
                _btnGenerate.Enabled = false;
                SetStatus("Shutdown complete.");
            }
        }
    }
}

