using System;
using System.IO;
using System.Text;

namespace SpireTerraria.Next {
 internal enum Operation : byte { Hello=1, Inspect=2, ItemIds=3, Ping=4, Shutdown=5, ItemStats=6, BattleStart=7, BattleStep=8, BattleEnd=9, AdventureStart=10, AdventureStep=11, ArenaDiagnostics=12, ItemIcons=13, SceneArt=14 }
 internal sealed class Packet {
  internal Operation Operation;
  internal int Sequence;
  internal bool IsResponse, IsError;
  internal byte[] Body;
 }
 internal static class Protocol {
  internal const uint Magic=0x32585453;
  internal const ushort Version=1;
  internal const int MaxBody=1024*1024;
  internal static byte[] Body(Action<BinaryWriter> write){using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,Encoding.UTF8,true)){write(w);w.Flush();}return s.ToArray();}}
  internal static BinaryReader Reader(Packet p){return new BinaryReader(new MemoryStream(p.Body,false),Encoding.UTF8);}
  internal static void Write(Stream stream,Packet packet){
   byte[] body=packet.Body??new byte[0];if(body.Length>MaxBody)throw new InvalidDataException("Frame too large.");
   using(var w=new BinaryWriter(stream,Encoding.UTF8,true)){w.Write(Magic);w.Write(Version);w.Write((byte)packet.Operation);w.Write((byte)((packet.IsResponse?1:0)|(packet.IsError?2:0)));w.Write(packet.Sequence);w.Write(body.Length);w.Write(body);w.Flush();}
  }
  internal static Packet Read(Stream stream){
   using(var r=new BinaryReader(stream,Encoding.UTF8,true)){
    if(r.ReadUInt32()!=Magic)throw new InvalidDataException("Invalid protocol magic.");
    if(r.ReadUInt16()!=Version)throw new InvalidDataException("Unsupported protocol version.");
    byte operation=r.ReadByte(),flags=r.ReadByte();
    if(operation<1||operation>14||(flags&~3)!=0)throw new InvalidDataException("Invalid frame header.");
    int sequence=r.ReadInt32(),length=r.ReadInt32();
    if(sequence<=0||length<0||length>MaxBody)throw new InvalidDataException("Invalid frame bounds.");
    byte[] body=r.ReadBytes(length);if(body.Length!=length)throw new EndOfStreamException("Incomplete frame.");
    return new Packet{Operation=(Operation)operation,Sequence=sequence,IsResponse=(flags&1)!=0,IsError=(flags&2)!=0,Body=body};
   }
  }
  internal static bool EqualSecret(string a,string b){
   if(a==null||b==null||a.Length!=b.Length)return false;int different=0;for(int i=0;i<a.Length;i++)different|=a[i]^b[i];return different==0;
  }
  internal static void RequireEnd(BinaryReader reader){if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Unexpected trailing data.");}
 }
}
