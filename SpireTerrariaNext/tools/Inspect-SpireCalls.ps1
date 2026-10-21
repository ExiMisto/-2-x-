param([string]$Type,[string]$Method,[string]$AssemblyPath='D:/steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll')
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Collections.Generic;
public static class SpireCalls {
 public static void Run(string typename,string methodname,string assemblyPath) {
  AssemblyLoadContext.Default.Resolving += (c,n)=>{var p=Path.Combine(@"D:/steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64",n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;};
  var a=Assembly.LoadFrom(assemblyPath);
  var t=a.GetType(typename,true); var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
  foreach(var m in t.GetMethods(flags)) if(m.Name==methodname) { Dump(m); var state=m.GetCustomAttribute<AsyncStateMachineAttribute>();if(state!=null)Dump(state.StateMachineType.GetMethod("MoveNext",flags)); }
 }
 static void Dump(MethodInfo m) {
  Console.WriteLine("METHOD "+m.DeclaringType+"."+m);var body=m.GetMethodBody();if(body==null)return;var b=body.GetILAsByteArray();var map=new Dictionary<short,OpCode>();foreach(var f in typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)){if(f.GetValue(null) is OpCode op)map[op.Value]=op;}
  for(int i=0;i<b.Length;) {int start=i;short code=b[i++];if(code==254)code=(short)(0xfe00|b[i++]);var op=map[code];string operand="";int size=0;
   switch(op.OperandType){case OperandType.InlineNone:break;case OperandType.ShortInlineI:case OperandType.ShortInlineVar:case OperandType.ShortInlineBrTarget:size=1;operand=b[i].ToString();break;case OperandType.InlineVar:size=2;operand=BitConverter.ToUInt16(b,i).ToString();break;case OperandType.InlineI:case OperandType.InlineBrTarget:case OperandType.ShortInlineR:size=4;operand=BitConverter.ToInt32(b,i).ToString();break;case OperandType.InlineI8:case OperandType.InlineR:size=8;operand=BitConverter.ToInt64(b,i).ToString();break;case OperandType.InlineSwitch:int count=BitConverter.ToInt32(b,i);size=4+count*4;operand="switch "+count;break;default:size=4;int token=BitConverter.ToInt32(b,i);try{if(op.OperandType==OperandType.InlineString)operand=m.Module.ResolveString(token);else {var member=m.Module.ResolveMember(token);operand=member.DeclaringType+" :: "+member;}}catch{operand="token "+token.ToString("x8");}break;}
   Console.WriteLine(start.ToString("x4")+" "+op.Name+" "+operand);i+=size;
  }
 }
}
'@
[SpireCalls]::Run($Type,$Method,$AssemblyPath)
