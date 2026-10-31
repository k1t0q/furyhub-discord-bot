using System;
using System.Drawing;
using System.Windows.Forms;
namespace ScumPositionProbe {
 public sealed class KeyCapture:Form {
  public Keys Selected;
  public static bool Allowed(Keys key){return ((key>=Keys.A&&key<=Keys.Z)||(key>=Keys.D0&&key<=Keys.D9)||(key>=Keys.F1&&key<=Keys.F24))&&key!=Keys.F7&&key!=Keys.F8&&key!=Keys.F9&&key!=Keys.F11;}
  public KeyCapture(){Text="Клавиша копирования";ClientSize=new Size(390,140);FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterParent;MaximizeBox=MinimizeBox=false;BackColor=Color.FromArgb(12,17,20);ForeColor=Color.White;Controls.Add(new Label{Text="Нажми клавишу, назначенную на копирование координат в SCUM.\n\nEsc — отмена. F7, F8, F9, F11 заняты программой.",Location=new Point(18,20),Size=new Size(354,110),Font=new Font("Segoe UI",10)});}
  protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==Keys.Escape){DialogResult=DialogResult.Cancel;return true;}if(Allowed(keyData)){Selected=keyData;DialogResult=DialogResult.OK;}return true;}
 }
}
