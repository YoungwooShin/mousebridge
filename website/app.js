const desk=document.querySelector('.desk');
document.querySelector('#demo-cross').addEventListener('click',()=>{desk.classList.toggle('moved');document.querySelector('#demo-cross').setAttribute('aria-pressed',String(desk.classList.contains('moved')));});
for(const [name,property,convert] of [['opacity','--alpha',x=>x/100],['width','--width',x=>x+'px'],['color','--glow',x=>x]]){const input=document.querySelector('#demo-'+name);input.addEventListener('input',()=>{desk.style.setProperty(property,convert(input.value));const out=document.querySelector('#'+name+'-value');if(out)out.textContent=input.value;});}
let downloadingWindows=false;
for(const button of document.querySelectorAll('.windows-download'))button.addEventListener('click',async event=>{
 event.preventDefault();if(downloadingWindows)return;downloadingWindows=true;
 const buttons=[...document.querySelectorAll('.windows-download')],labels=buttons.map(b=>b.textContent),korean=document.documentElement.lang==='ko';
 const status=document.querySelector('#download-status');
 const update=percent=>{for(const b of buttons){b.textContent=(korean?'다운로드 준비 중 ':'Preparing download ')+percent+'%';b.setAttribute('aria-disabled','true');}status.textContent=korean?'Windows 다운로드를 준비하고 있습니다.':'Preparing the Windows download.';};
 const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),180000);
 try{
  const base=new URL(document.documentElement.lang==='ko'?'../downloads/':'downloads/',location.href),r=await fetch(new URL('windows.json',base),{signal:controller.signal});if(!r.ok)throw Error('manifest');const manifest=await r.json();let received=0;
  update(0);
  const pieces=await Promise.all(manifest.parts.map(async part=>{const r=await fetch(new URL(part.name,base),{signal:controller.signal});if(!r.ok)throw Error('download');const reader=r.body.getReader(),chunks=[];let size=0;while(true){const {done,value}=await reader.read();if(done)break;chunks.push(value);size+=value.byteLength;received+=value.byteLength;update(Math.min(99,Math.floor(received/manifest.bytes*100)));}if(size!==part.bytes)throw Error('length');return new Blob(chunks);}));
  const file=new Blob(pieces,{type:'application/zip'});if(file.size!==manifest.bytes)throw Error('size');const digest=await crypto.subtle.digest('SHA-256',await file.arrayBuffer()),hash=[...new Uint8Array(digest)].map(x=>x.toString(16).padStart(2,'0')).join('');if(hash!==manifest.sha256)throw Error('checksum');
  const url=URL.createObjectURL(file),a=document.createElement('a');a.href=url;a.download=manifest.filename;document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),60000);status.textContent=korean?'다운로드를 시작했습니다. 압축을 풀고 MouseBridge.exe를 실행하세요.':'Download started. Extract and run MouseBridge.exe.';
 }catch(error){controller.abort();status.textContent=korean?'다운로드를 완료하지 못했습니다. 네트워크를 확인하고 다시 눌러 주세요.':'Download could not complete. Check the connection and try again.';}
 finally{clearTimeout(timeout);downloadingWindows=false;buttons.forEach((b,i)=>{b.textContent=labels[i];b.removeAttribute('aria-disabled');});}
});
