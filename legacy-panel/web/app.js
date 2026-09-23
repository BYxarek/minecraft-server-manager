const token=document.querySelector('meta[name="panel-token"]').content;
const $=id=>document.getElementById(id);
let lastStatus={};
const pageMeta={
  overview:['Обзор','Состояние выделенного сервера'],
  console:['Консоль','Команды и журнал Bedrock'],
  settings:['Настройки','Понятные параметры на русском языке'],
  players:['Игроки','Онлайн, белый список и уровни доступа'],
  worlds:['Миры и копии','Импорт, экспорт и восстановление'],
  packs:['Наборы','Ресурсы и наборы поведения']
};

async function api(path,options={}){
  const r=await fetch(path,{...options,headers:{'Content-Type':'application/json','X-Panel-Token':token,...options.headers}});
  let data;try{data=await r.json()}catch{data={}}
  if(!r.ok)throw new Error(data.error||`Ошибка HTTP ${r.status}`);
  return data;
}
async function uploadApi(path,file){
  const r=await fetch(path,{method:'POST',headers:{'Content-Type':'application/octet-stream','X-Panel-Token':token,'X-Filename':encodeURIComponent(file.name)},body:file});
  let data;try{data=await r.json()}catch{data={}}
  if(!r.ok)throw new Error(data.error||`Ошибка HTTP ${r.status}`);
  return data;
}
function notice(text,error=false){const el=$('notice');el.textContent=text;el.className=`notice${error?' error':''}`;clearTimeout(notice.timer);notice.timer=setTimeout(()=>el.classList.add('hidden'),6000)}
function duration(s){if(!s)return '—';const d=Math.floor(s/86400),h=Math.floor(s%86400/3600),m=Math.floor(s%3600/60);return [d&&`${d} д`,h&&`${h} ч`,`${m} мин`].filter(Boolean).join(' ')}
function esc(s=''){return String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
async function action(fn,success){try{const result=await fn();notice(success);await refreshStatus();return result}catch(e){notice(e.message,true);throw e}}

async function refreshStatus(){
  try{
    const s=await api('/api/status');lastStatus=s;
    $('statusBadge').textContent=s.running?'Работает':'Остановлен';$('statusBadge').className=`badge ${s.running?'on':'off'}`;
    $('version').textContent=s.version;$('clientVersion').textContent=`Клиент Bedrock ${s.clientVersion}`;$('panelVersion').textContent=s.panelVersion;
    $('memory').textContent=s.running?`${s.memoryMb} МБ`:'—';$('cpu').textContent=s.running?`CPU: ${s.cpuSeconds} с`:'Процесс не запущен';
    $('uptime').textContent=duration(s.uptimeSeconds);$('pid').textContent=s.pid?`PID ${s.pid}`:'—';$('port').textContent=`UDP ${s.port}`;$('transport').textContent=s.transport||'—';
    $('serverName').textContent=s.serverName||'Minecraft Server';$('levelName').textContent=s.levelName||'—';$('maxPlayers').textContent=s.maxPlayers||'—';
    $('managed').textContent=s.managed?'Запущен из панели':s.running?'Внешний процесс — только наблюдение':'Готов к запуску из панели';
    $('startBtn').disabled=s.running;$('stopBtn').disabled=!s.managed;$('restartBtn').disabled=!s.managed;$('command').disabled=!s.managed;document.querySelector('#commandForm button').disabled=!s.managed;
    if(s.running&&!s.managed)notice('Текущий сервер запущен вне панели. Введите stop в его консоли, затем нажмите «Запустить» здесь.');
  }catch(e){notice(e.message,true)}
}

document.querySelectorAll('nav button').forEach(btn=>btn.onclick=()=>{
  document.querySelectorAll('nav button,.page').forEach(x=>x.classList.remove('active'));btn.classList.add('active');$(btn.dataset.page).classList.add('active');
  [$('title').textContent,$('subtitle').textContent]=pageMeta[btn.dataset.page];
  if(btn.dataset.page==='overview'){loadMetrics();loadUpdate()}
  if(btn.dataset.page==='console')refreshLogs();
  if(btn.dataset.page==='settings')loadProperties();
  if(btn.dataset.page==='players')loadPlayers();
  if(btn.dataset.page==='worlds')loadWorlds();
  if(btn.dataset.page==='packs')loadPacks();
});

$('startBtn').onclick=()=>action(()=>api('/api/server/start',{method:'POST'}),'Сервер запущен.');
$('stopBtn').onclick=()=>action(()=>api('/api/server/stop',{method:'POST'}),'Сервер безопасно остановлен.');
$('restartBtn').onclick=()=>action(()=>api('/api/server/restart',{method:'POST'}),'Сервер перезапущен.');
$('commandForm').onsubmit=e=>{e.preventDefault();const command=$('command').value.trim();if(!command)return;action(()=>api('/api/command',{method:'POST',body:JSON.stringify({command})}),'Команда отправлена.').catch(()=>{});$('command').value=''};
async function refreshLogs(){try{const d=await api('/api/logs');$('logs').textContent=d.text||'Журнал пока пуст.';$('logs').scrollTop=$('logs').scrollHeight}catch(e){notice(e.message,true)}}
$('refreshLogs').onclick=refreshLogs;

const settingsUi=createSettingsUi({api,notice,action,esc});
const loadProperties=settingsUi.load;
$('saveProperties').onclick=settingsUi.save;

async function loadUpdate(){
  try{
    const u=await api('/api/update'),el=$('updateCard');
    if(u.updateAvailable){el.innerHTML=`Доступна новая версия Bedrock Server: <b>${esc(u.latest)}</b> (установлена ${esc(u.installed)}). <a href="${u.downloadPage}" target="_blank" rel="noopener">Официальная страница загрузки</a>`;el.classList.remove('hidden')}
    else el.classList.add('hidden');
  }catch{}
}
async function loadPanelConfig(){try{const c=await api('/api/panel/config');$('autoRestart').checked=c.autoRestart;$('autoRestartText').textContent=c.autoRestart?'Включено':'Выключено';$('restartAttempts').value=c.maxRestartAttempts}catch(e){notice(e.message,true)}}
$('autoRestart').onchange=()=>$('autoRestartText').textContent=$('autoRestart').checked?'Включено':'Выключено';
$('savePanelConfig').onclick=()=>action(()=>api('/api/panel/config',{method:'POST',body:JSON.stringify({autoRestart:$('autoRestart').checked,maxRestartAttempts:Number($('restartAttempts').value)})}),'Настройки автоперезапуска сохранены.').catch(()=>{});

async function loadMetrics(){try{const d=await api('/api/metrics');drawMetrics(d.metrics)}catch(e){notice(e.message,true)}}
function drawMetrics(points){
  const canvas=$('metricsChart'),dpr=window.devicePixelRatio||1,w=canvas.clientWidth||600,h=210;canvas.width=w*dpr;canvas.height=h*dpr;
  const c=canvas.getContext('2d');c.scale(dpr,dpr);c.clearRect(0,0,w,h);c.strokeStyle='#303830';c.lineWidth=1;
  for(let i=1;i<4;i++){const y=i*h/4;c.beginPath();c.moveTo(0,y);c.lineTo(w,y);c.stroke()}
  if(points.length<2){c.fillStyle='#9ca69a';c.font='13px Segoe UI';c.fillText('Данные появятся после двух замеров (около минуты).',14,28);return}
  const maxMem=Math.max(256,...points.map(p=>p.memoryMb||0)),series=[['memoryMb',maxMem,'#67c36b'],['cpuPercent',100,'#e0a85a']];
  for(const [key,max,color] of series){c.strokeStyle=color;c.lineWidth=2;c.beginPath();points.forEach((p,i)=>{const x=i/(points.length-1)*(w-8)+4,y=h-8-(Math.min(max,Number(p[key])||0)/max)*(h-16);i?c.lineTo(x,y):c.moveTo(x,y)});c.stroke()}
}

function allowedRow(p={name:'',xuid:'',ignoresPlayerLimit:false}){const div=document.createElement('div');div.className='row';div.innerHTML=`<input data-k="name" placeholder="Gamertag" value="${esc(p.name)}"><input data-k="xuid" placeholder="XUID (необязательно)" value="${esc(p.xuid||'')}"><button class="danger" title="Удалить">×</button><label><input data-k="ignoresPlayerLimit" type="checkbox" ${p.ignoresPlayerLimit?'checked':''}> Вход сверх лимита</label>`;div.querySelector('button').onclick=()=>div.remove();return div}
function permissionRow(p={xuid:'',permission:'member'}){const div=document.createElement('div');div.className='row permission';div.innerHTML=`<input data-k="xuid" placeholder="Числовой XUID" value="${esc(p.xuid)}"><select data-k="permission"><option value="visitor">Посетитель</option><option value="member">Участник</option><option value="operator">Оператор</option></select><button class="danger" title="Удалить">×</button>`;div.querySelector('select').value=p.permission;div.querySelector('button').onclick=()=>div.remove();return div}
async function loadOnlinePlayers(){
  try{const d=await api('/api/players/online');$('onlinePlayers').innerHTML=d.players.length?d.players.map(name=>`<div class="list-item"><b>${esc(name)}</b><div class="item-actions"><button data-player="${esc(name)}" data-action="op">Оператор</button><button data-player="${esc(name)}" data-action="deop">Снять права</button><button class="danger" data-player="${esc(name)}" data-action="kick">Отключить</button></div></div>`).join(''):'<p class="empty">Игроков онлайн нет.</p>';$('onlinePlayers').querySelectorAll('[data-action]').forEach(b=>b.onclick=()=>action(()=>api('/api/players/action',{method:'POST',body:JSON.stringify({action:b.dataset.action,player:b.dataset.player})}),'Команда игроку отправлена.').then(loadOnlinePlayers).catch(()=>{}))}catch(e){$('onlinePlayers').innerHTML=`<p class="empty">${esc(e.message)}</p>`}
}
async function loadPlayers(){try{const [a,p]=await Promise.all([api('/api/allowlist'),api('/api/permissions')]);$('allowlist').replaceChildren(...a.players.map(allowedRow));$('permissions').replaceChildren(...p.players.map(permissionRow));loadOnlinePlayers()}catch(e){notice(e.message,true)}}
$('refreshPlayers').onclick=loadOnlinePlayers;$('addAllowed').onclick=()=>$('allowlist').append(allowedRow());$('addPermission').onclick=()=>$('permissions').append(permissionRow());
function collectRows(parent){return [...parent.children].map(row=>Object.fromEntries([...row.querySelectorAll('[data-k]')].map(el=>[el.dataset.k,el.type==='checkbox'?el.checked:el.value.trim()])))}
$('saveAllowlist').onclick=()=>action(()=>api('/api/allowlist',{method:'POST',body:JSON.stringify({players:collectRows($('allowlist'))})}),'Белый список сохранён.').catch(()=>{});
$('savePermissions').onclick=()=>action(()=>api('/api/permissions',{method:'POST',body:JSON.stringify({players:collectRows($('permissions'))})}),'Права сохранены; применятся после перезапуска.').catch(()=>{});

async function loadWorlds(){
  try{
    const [worldData,backupData]=await Promise.all([api('/api/worlds'),api('/api/backups')]),worlds=worldData.worlds,backups=backupData.backups;
    $('worldList').innerHTML=worlds.length?worlds.map(w=>`<div class="list-item"><div><b class="${w.active?'active-world':''}">${esc(w.name)}${w.active?' · активен':''}</b><small>${w.sizeMb} МБ · ${new Date(w.modified).toLocaleString('ru-RU')}</small></div><div class="item-actions">${w.active?'':`<button data-world="${esc(w.name)}" data-world-action="select">Выбрать</button>`}<button data-world="${esc(w.name)}" data-world-action="export">Экспорт</button></div></div>`).join(''):'<p class="empty">Миры не найдены.</p>';
    $('worldList').querySelectorAll('[data-world-action="select"]').forEach(b=>b.onclick=()=>action(()=>api('/api/world/select',{method:'POST',body:JSON.stringify({name:b.dataset.world})}),'Активный мир изменён.').then(loadWorlds).catch(()=>{}));
    $('worldList').querySelectorAll('[data-world-action="export"]').forEach(b=>b.onclick=()=>action(()=>api('/api/worlds/export',{method:'POST',body:JSON.stringify({name:b.dataset.world})}),'Архив мира подготовлен.').then(x=>{location.href=`/api/backups/download/${encodeURIComponent(x.name)}`;loadWorlds()}).catch(()=>{}));
    $('backupList').innerHTML=backups.length?backups.map(b=>`<div class="list-item"><div><b>${esc(b.name)}</b><small>${b.sizeMb} МБ · ${new Date(b.created).toLocaleString('ru-RU')}</small></div><div class="item-actions"><a href="/api/backups/download/${encodeURIComponent(b.name)}">Скачать</a><button class="danger" data-restore="${esc(b.name)}">Восстановить</button></div></div>`).join(''):'<p class="empty">Резервных копий пока нет.</p>';
    $('backupList').querySelectorAll('[data-restore]').forEach(b=>b.onclick=()=>{if(confirm('Текущий мир будет заменён. Перед этим панель создаст страховочную копию. Продолжить?'))action(()=>api('/api/backups/restore',{method:'POST',body:JSON.stringify({name:b.dataset.restore})}),'Мир восстановлен.').then(loadWorlds).catch(()=>{})});
  }catch(e){notice(e.message,true)}
}
$('createBackup').onclick=()=>action(()=>api('/api/backups',{method:'POST'}),'Резервная копия создана.').then(loadWorlds).catch(()=>{});
$('importWorld').onclick=()=>$('worldFile').click();
$('worldFile').onchange=()=>{const file=$('worldFile').files[0];if(file)action(()=>uploadApi('/api/worlds/import',file),`Мир «${file.name}» импортирован.`).then(loadWorlds).catch(()=>{});$('worldFile').value=''};

async function loadPacks(){
  try{const d=await api('/api/packs');$('packList').innerHTML=d.packs.length?d.packs.map(p=>`<div class="list-item"><div><span class="pack-kind">${p.kind==='resource'?'Ресурсы':'Поведение'}</span><b>${esc(p.name||p.folder)}</b><small>Версия ${esc(p.version)} · ${p.enabled?'включён':'выключен'}</small></div><div class="item-actions"><button data-pack-toggle="${esc(p.id)}" data-kind="${p.kind}" data-enabled="${!p.enabled}">${p.enabled?'Выключить':'Включить'}</button><button class="danger" data-pack-remove="${esc(p.folder)}" data-kind="${p.kind}">Удалить</button></div></div>`).join(''):'<p class="empty">Наборы не найдены.</p>';$('packList').querySelectorAll('[data-pack-toggle]').forEach(b=>b.onclick=()=>action(()=>api('/api/packs/toggle',{method:'POST',body:JSON.stringify({id:b.dataset.packToggle,kind:b.dataset.kind,enabled:b.dataset.enabled==='true'})}),'Состояние набора изменено.').then(loadPacks).catch(()=>{}));$('packList').querySelectorAll('[data-pack-remove]').forEach(b=>b.onclick=()=>{if(confirm('Убрать этот набор? Его файлы останутся в корзине панели.'))action(()=>api('/api/packs/remove',{method:'POST',body:JSON.stringify({folder:b.dataset.packRemove,kind:b.dataset.kind})}),'Набор перемещён в корзину.').then(loadPacks).catch(()=>{})})}catch(e){notice(e.message,true)}
}
$('refreshPacks').onclick=loadPacks;$('importPack').onclick=()=>$('packFile').click();
$('packFile').onchange=()=>{const file=$('packFile').files[0];if(file)action(()=>uploadApi('/api/packs/import',file),`Набор «${file.name}» установлен.`).then(loadPacks).catch(()=>{});$('packFile').value=''};

refreshStatus();loadPanelConfig();loadUpdate();loadMetrics();
setInterval(()=>{refreshStatus();if($('console').classList.contains('active'))refreshLogs();if($('overview').classList.contains('active'))loadMetrics()},5000);
