// Only a few icons need PNG copies. All player/projectile XNB artwork stays in Terraria.
const fs=require('node:fs'),path=require('node:path');
(async()=>{const [game,dest,lib]=process.argv.slice(2);const xnb=require(path.resolve(lib));fs.mkdirSync(dest,{recursive:true});
 for(const [source,target] of [[24,24],[39,39],[3069,3069],[154,6143]]){
  const raw=fs.readFileSync(path.join(game,'Content/Images/Item_'+source+'.xnb'));
  const files=await xnb.unpackToFiles({buffer:raw.buffer.slice(raw.byteOffset,raw.byteOffset+raw.byteLength)},{fileName:'Item_'+source+'.xnb',contentOnly:true});
  fs.writeFileSync(path.join(dest,'Item_'+target+'.png'),Buffer.from(await files.find(x=>x.extension==='png').data.arrayBuffer()));
 }console.log('Prepared four local icons');
})().catch(e=>{console.error(e);process.exitCode=1;});
