window.createSettingsUi=({api,notice,action,esc})=>{
let originalRaw='';
const groups=[
['Основное',[
['server-name','Название сервера','Так сервер отображается в списке серверов.','text'],
['gamemode','Режим игры','Режим для новых игроков.','select',{survival:'Выживание',creative:'Творческий',adventure:'Приключение'}],
['difficulty','Сложность','Влияет на урон, голод и появление враждебных существ.','select',{peaceful:'Мирная',easy:'Лёгкая',normal:'Нормальная',hard:'Сложная'}],
['allow-cheats','Команды и читы','Разрешает игровые команды, телепортацию и изменение правил мира.','bool'],
['max-players','Максимум игроков','Сколько игроков может находиться на сервере одновременно.','number',{min:1,max:1000}],
['player-idle-timeout','Отключать бездействующих','Минуты до отключения неактивного игрока; 0 — никогда.','number',{min:0}],
['default-player-permission-level','Права новых игроков','Что разрешено игроку при первом входе.','select',{visitor:'Посетитель',member:'Участник',operator:'Оператор'}],
['force-gamemode','Принудительный режим','Всегда переключать вошедшего игрока на выбранный выше режим.','bool']]],
['Мир и видимость',[
['level-name','Активный мир','Имя папки мира. Для переключения удобнее раздел «Миры».','text'],
['level-seed','Ключ генерации','Используется только при создании нового мира; можно оставить пустым.','text'],
['view-distance','Дальность прорисовки','Максимум в чанках. Больше — красивее, но тяжелее для сервера и сети.','number',{min:5,max:96}],
['tick-distance','Дальность симуляции','Радиус работы мобов, механизмов и растений: от 4 до 12 чанков.','number',{min:4,max:12}],
['texturepack-required','Требовать наборы ресурсов','Не пускать игрока, если он отказался загрузить ресурсы мира.','bool'],
['client-side-chunk-generation-enabled','Генерация чанков на клиенте','Снижает нагрузку сервера при показе дальних чанков.','bool'],
['server-build-radius-ratio','Доля генерации сервером','Disabled — автоматически; число 0–1 задаёт долю вручную.','text']]],
['Доступ и сеть',[
['online-mode','Проверять Xbox Live','Проверяет учётные записи. Для интернет-сервера отключать небезопасно.','bool'],
['allow-list','Белый список','Пускать только игроков из раздела «Игроки».','bool'],
['server-port','Порт IPv4','Основной UDP-порт; обычно 19132.','number',{min:1,max:65535}],
['server-portv6','Порт IPv6','UDP-порт IPv6; обычно 19133. В NetherNet отдельно не используется.','number',{min:1,max:65535}],
['transport','Сетевой транспорт','NetherNet — актуальный для этой версии; RakNet нужен для старых схем.','select',{nethernet:'NetherNet',raknet:'RakNet'}],
['enable-lan-visibility','Видимость в локальной сети','Показывать сервер устройствам в той же домашней сети.','bool'],
['compression-algorithm','Сжатие трафика','Zlib экономит трафик, Snappy меньше нагружает процессор.','select',{zlib:'Zlib — меньше трафика',snappy:'Snappy — меньше нагрузка'}],
['compression-threshold','Порог сжатия','Пакеты от этого размера в байтах будут сжиматься; 0 — все.','number',{min:0,max:65535}]]],
['Защита игроков',[
['server-authoritative-movement-strict','Строгая проверка перемещения','Ловит неверные координаты, но при плохом пинге чаще возвращает игрока назад.','bool'],
['server-authoritative-dismount-strict','Строгая проверка спешивания','Проверяет позицию после выхода из лодок, вагонеток и с ездовых существ.','bool'],
['server-authoritative-entity-interactions-strict','Строгая проверка взаимодействий','Точнее проверяет дистанцию взаимодействия; чувствительна к задержке.','bool'],
['player-position-acceptance-threshold','Допуск координат','Больше — мягче к задержкам, но слабее защита от неверных координат.','number',{min:0,step:0.1}],
['player-movement-action-direction-threshold','Допуск направления атаки','От 0 до 1: единица требует точного совпадения взгляда и атаки.','number',{min:0,max:1,step:0.05}],
['server-authoritative-block-breaking-pick-range-scalar','Дальность добычи блоков','Множитель допустимой дистанции разрушения блока.','number',{min:0,step:0.1}],
['disable-player-interaction','Запрет взаимодействия игроков','Клиенты игнорируют взаимодействия игроков друг с другом.','bool'],
['disable-custom-skins','Запрет сторонних скинов','Отключает скины вне магазина и встроенного редактора.','bool']]],
['Чат, журнал и нагрузка',[
['chat-restriction','Ограничение чата','Можно скрыть сообщения или убрать чат у обычных игроков.','select',{None:'Без ограничений',Dropped:'Не доставлять сообщения',Disabled:'Отключить интерфейс чата'}],
['content-log-file-enabled','Журнал ошибок контента','Записывать ошибки наборов поведения и ресурсов в файл.','bool'],
['content-log-console-output-enabled','Ошибки контента в консоли','Показывать ошибки наборов прямо в консоли панели.','bool'],
['content-log-level','Подробность журнала','Минимальный уровень сообщений, попадающих в журнал.','select',{error:'Только ошибки',warning:'Ошибки и предупреждения',info:'Информация',verbose:'Максимально подробно'}],
['max-threads','Потоки процессора','0 — подобрать автоматически; другое число задаёт ограничение.','number',{min:0,max:256}],
['block-network-ids-are-hashes','Стабильные ID блоков','Обычно лучше оставить включённым для стабильности сетевых ID.','bool']]],
['Скрипты и отладка',[
['allow-inbound-script-debugging','Входящая отладка','Разрешает VS Code подключаться к серверу. Включайте только при необходимости.','bool'],
['allow-outbound-script-debugging','Исходящая отладка','Разрешает серверу подключаться к внешнему отладчику.','bool'],
['script-debugger-auto-attach','Автоподключение отладчика','Режим отладчика при загрузке мира.','select',{disabled:'Отключено',listen:'Ждать подключение',connect:'Подключиться к отладчику'}]]]
];
function control(def,value){const [key,,,type,opts={}]=def;if(type==='bool')return `<label class="switch"><span>${value==='true'?'Включено':'Выключено'}</span><input data-setting="${key}" type="checkbox" ${value==='true'?'checked':''}></label>`;if(type==='select')return `<select data-setting="${key}">${Object.entries(opts).map(([v,n])=>`<option value="${esc(v)}" ${v===value?'selected':''}>${esc(n)}</option>`).join('')}</select>`;const attrs=type==='number'?Object.entries(opts).map(([k,v])=>`${k}="${v}"`).join(' '):'';return `<input data-setting="${key}" type="${type}" value="${esc(value??'')}" ${attrs}>`}
function render(values){document.getElementById('settingsForm').innerHTML=groups.map(([group,defs])=>`<section class="settings-group"><h3>${group}</h3><div class="settings-grid">${defs.map(def=>`<article class="setting"><div><label>${def[1]}</label><p>${def[2]}</p></div>${control(def,values[def[0]])}</article>`).join('')}</div></section>`).join('');document.querySelectorAll('.switch input').forEach(input=>input.onchange=()=>input.previousElementSibling.textContent=input.checked?'Включено':'Выключено')}
async function load(){try{const d=await api('/api/properties');originalRaw=d.raw;document.getElementById('propertiesRaw').value=d.raw;render(d.values)}catch(e){notice(e.message,true)}}
function escapeRegex(s){return s.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')}
function formRaw(){let raw=originalRaw;document.querySelectorAll('[data-setting]').forEach(el=>{const value=el.type==='checkbox'?String(el.checked):el.value,key=el.dataset.setting,re=new RegExp(`^${escapeRegex(key)}=.*$`,'m');raw=re.test(raw)?raw.replace(re,`${key}=${value}`):`${raw.trimEnd()}\r\n${key}=${value}\r\n`});return raw}
function save(){const raw=document.querySelector('.advanced').open?document.getElementById('propertiesRaw').value:formRaw();return action(()=>api('/api/properties',{method:'POST',body:JSON.stringify({raw})}),'Настройки сохранены. Перезапустите сервер для применения.').then(load)}
return {load,save};
};
