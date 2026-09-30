using System.Diagnostics;

namespace LethalModpackUpdater;

public sealed class MainForm : Form
{
    private readonly LocalState state = Data.LoadState();
    private readonly Label pathLabel = new();
    private readonly Label statusLabel = new();
    private readonly Button updateButton = new();
    private readonly Button chooseButton = new();

    public MainForm()
    {
        Text = "Lethal Company · Modpack";
        ClientSize = new Size(520, 205);
        MinimumSize = new Size(470, 230);
        MaximumSize = new Size(900, 230);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(25, 27, 32);
        ForeColor = Color.White;

        var title = new Label { Text = "Modpack dos amigos", Font = new Font("Segoe UI", 17, FontStyle.Bold),
            Location = new Point(20, 17), Size = new Size(470, 37) };
        pathLabel.Location = new Point(20, 61);
        pathLabel.Size = new Size(385, 42);
        pathLabel.ForeColor = Color.Gainsboro;
        pathLabel.AutoEllipsis = true;
        chooseButton.Text = "Escolher pasta";
        chooseButton.Location = new Point(400, 59);
        chooseButton.Size = new Size(105, 35);
        chooseButton.Click += (_, _) => ChooseFolder();

        updateButton.Text = "Atualizar e jogar";
        updateButton.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        updateButton.Location = new Point(20, 111);
        updateButton.Size = new Size(485, 47);
        updateButton.BackColor = Color.FromArgb(89, 153, 113);
        updateButton.ForeColor = Color.Black;
        updateButton.FlatStyle = FlatStyle.Flat;
        updateButton.Click += async (_, _) => await UpdateAndPlayAsync();

        statusLabel.Location = new Point(20, 171);
        statusLabel.Size = new Size(485, 23);
        statusLabel.ForeColor = Color.Silver;
        Controls.AddRange([title, pathLabel, chooseButton, updateButton, statusLabel]);
        RefreshPath();
        statusLabel.Text = state.Tag is null ? "Primeira instalação: será baixado o pacote completo." : $"Versão instalada: {state.Tag}";
    }

    private void RefreshPath() => pathLabel.Text = string.IsNullOrEmpty(state.GamePath)
        ? "Escolha a pasta que contém Lethal Company.exe"
        : state.GamePath;

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Selecione a pasta do Lethal Company",
            UseDescriptionForTitle = true, InitialDirectory = Directory.Exists(state.GamePath) ? state.GamePath : "" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!File.Exists(Path.Combine(dialog.SelectedPath, "Lethal Company.exe")))
        {
            MessageBox.Show(this, "Essa pasta não contém Lethal Company.exe.", "Pasta inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!Path.GetFullPath(dialog.SelectedPath).Equals(Path.GetFullPath(state.GamePath.Length == 0 ? dialog.SelectedPath : state.GamePath), StringComparison.OrdinalIgnoreCase))
        {
            state.Tag = null;
            state.Files.Clear();
        }
        state.GamePath = dialog.SelectedPath;
        Data.SaveState(state);
        RefreshPath();
    }

    private async Task UpdateAndPlayAsync()
    {
        if (!File.Exists(Path.Combine(state.GamePath, "Lethal Company.exe")))
        {
            ChooseFolder();
            if (!File.Exists(Path.Combine(state.GamePath, "Lethal Company.exe"))) return;
        }
        updateButton.Enabled = false;
        chooseButton.Enabled = false;
        try
        {
            var updater = new Updater(message => BeginInvoke(() => statusLabel.Text = message));
            var result = await Task.Run(() => updater.UpdateAsync(state));
            statusLabel.Text = result;
            Process.Start(new ProcessStartInfo(Path.Combine(state.GamePath, "Lethal Company.exe"))
            {
                WorkingDirectory = state.GamePath,
                UseShellExecute = true
            });
            Close();
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Não foi possível atualizar.";
            MessageBox.Show(this, ex.Message, "Falha na atualização", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            updateButton.Enabled = true;
            chooseButton.Enabled = true;
        }
    }
}
