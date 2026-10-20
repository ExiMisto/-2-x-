// Local adapter for Hatsunetrump/TerrariaICEY's native worker (see THIRD-PARTY.md).
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TerrariaICEY.Bridge;

namespace SpireTerraria {
 public sealed class NativeBridge : IDisposable {
  TcpClient client; TcpListener listener; BinaryReader reader; BinaryWriter writer; Process process;
  readonly StringBuilder diagnostics = new StringBuilder();
  public string Version;
  public NativeBridge(string root, string terraria,bool tests=false) {
   string saves=Path.Combine(root,"saves"); Directory.CreateDirectory(saves);
   listener=new TcpListener(IPAddress.Loopback,0); listener.Start(1);
   int port=((IPEndPoint)listener.LocalEndpoint).Port; string token=Guid.NewGuid().ToString("N");
   var start=new ProcessStartInfo {
    FileName=Path.Combine(root,"runtime","TerrariaEngine.exe"),
    Arguments=Quote(terraria)+" "+Quote(saves)+" --server "+port+" "+token+" "+Quote(Path.Combine(root,"assets"))+" --graphics --owner-process "+Process.GetCurrentProcess().Id+" --windows-backend"+(tests?" --spire-tests":""),
    WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
    RedirectStandardOutput=true,RedirectStandardError=true
   };
   try {
    process=new Process{StartInfo=start};
    process.OutputDataReceived+=(s,e)=>Append(e.Data); process.ErrorDataReceived+=(s,e)=>Append(e.Data);
    process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
    var accept=listener.AcceptTcpClientAsync();var timer=Stopwatch.StartNew();
    while(!accept.Wait(100)) {if(process.HasExited)throw new IOException("Native worker exited: "+Log);if(timer.ElapsedMilliseconds>90000)throw new TimeoutException("Native worker startup: "+Log);}
    client=accept.Result;client.NoDelay=true;client.ReceiveTimeout=30000;client.SendTimeout=30000;
    var stream=client.GetStream();reader=new BinaryReader(new BufferedStream(stream,65536));writer=new BinaryWriter(new BufferedStream(stream,65536));
    string protocol=reader.ReadString(),received=reader.ReadString();Version=reader.ReadString();
    if(protocol!="TerrariaICEY.Native.v45"||received!=token)throw new IOException("Native handshake mismatch");
   } catch {Dispose();throw;} finally {listener.Stop();}
  }
  static string Quote(string value){return "\""+value.Replace("\"","")+"\"";}
  void Append(string value){if(value!=null)lock(diagnostics)diagnostics.AppendLine(value);}
  public string Log {get{lock(diagnostics)return diagnostics.ToString();}}
  void Check(byte expected){byte reply=reader.ReadByte();if(reply==2)throw new IOException(reader.ReadString());if(reply!=expected)throw new IOException("Native reply "+reply+", expected "+expected);}
  public EngineOutput Step(EngineInput input){writer.Write((byte)1);input.Write(writer);writer.Flush();Check(1);return EngineOutput.Read(reader);}
  public void Configure(BattleSetup setup){writer.Write((byte)80);setup.Write(writer);writer.Flush();Check(80);}
  public BattleState Battle(){writer.Write((byte)81);writer.Flush();Check(81);return BattleState.Read(reader);}
  public void TestStrikeEnemies(){writer.Write((byte)82);writer.Flush();Check(82);}
  public int ExportAssets(){writer.Write((byte)83);writer.Flush();Check(83);return reader.ReadInt32();}
  public bool UsePotion(int kind){writer.Write((byte)84);writer.Write(kind);writer.Flush();Check(84);return reader.ReadBoolean();}
  public void ArtMode(int mode){writer.Write((byte)85);writer.Write(mode);writer.Flush();Check(85);}
  public void ClearRoom(){writer.Write((byte)86);writer.Flush();Check(86);}
  public int TestDrop(){writer.Write((byte)87);writer.Flush();Check(87);return reader.ReadInt32();}
  public RenderFrame Render(RenderRequest request){writer.Write((byte)3);request.Write(writer);writer.Flush();Check(3);return RenderFrame.Read(reader);}
  public void Export(string filename){writer.Write((byte)15);writer.Write(filename);writer.Flush();Check(15);}
  public void Dispose(){
   try{if(writer!=null){writer.Write((byte)255);writer.Flush();}}catch{}
   try{if(client!=null)client.Close();}catch{}
   try{if(listener!=null)listener.Stop();}catch{}
   try{if(process!=null&&!process.HasExited&&!process.WaitForExit(2000))process.Kill();}catch{}
   if(process!=null)process.Dispose();
  }
 }
 public static class Arena {
  public const int Width=960,Height=540;public const float ViewX=7712,ViewY=3676,Floor=4096;
  public static EngineInput Input(bool reset) {
   var i=new EngineInput{Reset=reset,FullReset=reset,ProfileChanged=reset,Unlimited=true,
    X=ViewX+200,Y=Floor,MouseX=ViewX+620,MouseY=Floor-45,ConstrainPlayer=true,
    MinX=ViewX+24,MaxX=ViewX+936,MinY=ViewY+80,MaxY=Floor,CharacterScale=1.5f,WeaponScale=1.3f};
   if(reset){i.Inventory[0]=24;i.Inventory[1]=39;i.Inventory[2]=3069;for(int n=0;n<3;n++)i.InventoryStacks[n]=1;
    i.Ammo[0]=40;i.AmmoStacks[0]=9999;
    i.TileX=480;i.TileY=228;i.TileWidth=64;i.TileHeight=34;i.Tiles=new byte[64*34];
    for(int y=28;y<34;y++)for(int x=0;x<64;x++)i.Tiles[y*64+x]=1;
   }
   return i;
  }
  public static RenderRequest Render(){return new RenderRequest{Width=Width,Height=Height,X=ViewX,Y=ViewY,Compact=true,Bgra=false,Hair=1,
   Colors=new uint[]{0xff80b0e8,0xff8c6b36,0xff192e4c,0xffa6b24f,0xff635c2e,0xff6b4c4c,0xff212e3d},CharacterScale=1.5f};}
  public static EngineInput CampaignInput(BattleSetup setup,bool reset){
   var i=new EngineInput{Reset=reset,FullReset=reset,Unlimited=false,ProfileChanged=false,ConstrainPlayer=true,MinX=setup.Left+24,MaxX=setup.Left+setup.Width-24,MinY=setup.Top+24,MaxY=setup.Floor,X=setup.Left+setup.Width*.5f,Y=setup.Floor,CharacterScale=1.25f,WeaponScale=1};
   if(reset){
    i.TileX=(int)setup.Left/16;i.TileY=(int)setup.Top/16;i.TileWidth=setup.Width/16;i.TileHeight=setup.Height/16+8;i.Tiles=new byte[i.TileWidth*i.TileHeight];
    for(int y=setup.Height/16;y<i.TileHeight;y++)for(int x=0;x<i.TileWidth;x++)i.Tiles[y*i.TileWidth+x]=1;
    if(setup.Kind==BattleKind.Boss)for(int row=1;row<=3;row++){int y=setup.Height/16-row*12;for(int x=4;x<i.TileWidth-4;x++)if(x%24<17)i.Tiles[y*i.TileWidth+x]=2;}
   }
   return i;
  }
  public static RenderRequest CampaignRender(BattleSetup setup,EngineOutput output){
   var q=Render();q.CharacterScale=1.25f;
   q.X=Math.Max(setup.Left,Math.Min(setup.Left+setup.Width-Width,(output==null?setup.Left+setup.Width*.5f:output.X)-Width*.5f));
   q.Y=Math.Max(setup.Top,Math.Min(setup.Floor-420,(output==null?setup.Floor:output.Y)-420));return q;
  }
 }
}
