using System;
using System.Windows.Forms;
using System.Drawing;

namespace WeatherTrayApp;

public class LocationDialog : Form
{
    public string LocationName { get; private set; } = "";

    private TextBox _textBox;
    private Button _okButton;
    private Button _cancelButton;

    public LocationDialog(string currentName)
    {
        Text = "Enter Location";
        Size = new Size(300, 150);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "City, Country (e.g. London, UK):",
            Left = 10,
            Top = 10,
            Width = 260
        };

        _textBox = new TextBox
        {
            Text = currentName,
            Left = 10,
            Top = 35,
            Width = 260
        };

        _okButton = new Button
        {
            Text = "OK",
            Left = 110,
            Width = 80,
            Top = 70,
            DialogResult = DialogResult.OK
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            Left = 200,
            Width = 80,
            Top = 70,
            DialogResult = DialogResult.Cancel
        };

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.Add(label);
        Controls.Add(_textBox);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);

        FormClosing += (s, e) =>
        {
            if (DialogResult == DialogResult.OK)
            {
                LocationName = _textBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(LocationName))
                {
                    MessageBox.Show("Please enter a valid location.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    e.Cancel = true;
                }
            }
        };
    }
}
