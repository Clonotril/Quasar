namespace Quasar.Server.Forms
{
    partial class FrmRemoteChat
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.rtxtChat = new System.Windows.Forms.RichTextBox();
            this.txtInput = new System.Windows.Forms.TextBox();
            this.btnSend = new System.Windows.Forms.Button();
            this.panelBottom = new System.Windows.Forms.Panel();
            this.panelBottom.SuspendLayout();
            this.SuspendLayout();
            //
            // rtxtChat
            //
            this.rtxtChat.BackColor = System.Drawing.Color.White;
            this.rtxtChat.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtxtChat.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.rtxtChat.Location = new System.Drawing.Point(0, 0);
            this.rtxtChat.Name = "rtxtChat";
            this.rtxtChat.ReadOnly = true;
            this.rtxtChat.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.rtxtChat.Size = new System.Drawing.Size(434, 301);
            this.rtxtChat.TabIndex = 0;
            this.rtxtChat.Text = "";
            //
            // txtInput
            //
            this.txtInput.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtInput.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.txtInput.Location = new System.Drawing.Point(2, 2);
            this.txtInput.Name = "txtInput";
            this.txtInput.Size = new System.Drawing.Size(360, 23);
            this.txtInput.TabIndex = 1;
            this.txtInput.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtInput_KeyDown);
            //
            // btnSend
            //
            this.btnSend.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnSend.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnSend.Location = new System.Drawing.Point(362, 2);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(70, 28);
            this.btnSend.TabIndex = 2;
            this.btnSend.Text = "Send";
            this.btnSend.UseVisualStyleBackColor = true;
            this.btnSend.Click += new System.EventHandler(this.btnSend_Click);
            //
            // panelBottom
            //
            this.panelBottom.Controls.Add(this.txtInput);
            this.panelBottom.Controls.Add(this.btnSend);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(0, 301);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Padding = new System.Windows.Forms.Padding(2);
            this.panelBottom.Size = new System.Drawing.Size(434, 32);
            this.panelBottom.TabIndex = 3;
            //
            // FrmRemoteChat
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(434, 333);
            this.Controls.Add(this.rtxtChat);
            this.Controls.Add(this.panelBottom);
            this.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MinimumSize = new System.Drawing.Size(350, 280);
            this.Name = "FrmRemoteChat";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Remote Chat";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrmRemoteChat_FormClosing);
            this.Load += new System.EventHandler(this.FrmRemoteChat_Load);
            this.panelBottom.ResumeLayout(false);
            this.panelBottom.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.RichTextBox rtxtChat;
        private System.Windows.Forms.TextBox txtInput;
        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.Panel panelBottom;
    }
}
