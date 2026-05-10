using System;
using System.Drawing;
using System.Windows.Forms;

namespace Интерпретатор_машины_Тьюринга
{
    public class TapeControl : Panel
    {
        public readonly TapeRenderer tapeRenderer;
        private readonly Button btnLeft;
        private readonly Button btnRight;
        public TapeControl()
        {
            Width = 280;
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(0);
            Margin = new Padding(0);
            btnLeft = new Button
            {
                Text = "←",
                Width = 20,
                Dock = DockStyle.Left,
                Font = new System.Drawing.Font("Arial", 10, FontStyle.Bold),
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            btnRight = new Button
            {
                Text = "→",
                Width = 20,
                Dock = DockStyle.Right,
                Font = new System.Drawing.Font("Arial", 10, FontStyle.Bold),
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            tapeRenderer = new TapeRenderer
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            btnLeft.Click += (s, e) =>
            {
                tapeRenderer.MoveHeadLeft();
                this.Focus();
            };
            btnRight.Click += (s, e) =>
            {
                tapeRenderer.MoveHeadRight();
                this.Focus();
            };
            Controls.Add(btnLeft);
            Controls.Add(btnRight);
            Controls.Add(tapeRenderer);
            tapeRenderer.ValueChanged += (s, e) => OnValueChanged(EventArgs.Empty);
        }
        public event EventHandler ValueChanged;

        protected virtual void OnValueChanged(EventArgs e)
        {
            if (ValueChanged != null)
            {
                ValueChanged(this, e);
            }
        }
        public void SetHeadPosition(int position)
        {
            tapeRenderer.SetHeadPosition(position);
        }
        public void InitializeTapeContent(string initialContent)
        {
            tapeRenderer.InitializeTapeContent(initialContent);
        }
    }
}
