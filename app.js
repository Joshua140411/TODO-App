'use strict';
const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
const statusLabels = {Open:'Offen', InProgress:'In Bearbeitung', Done:'Erledigt', Cancelled:'Abgebrochen'};
const priorityLabels = {Low:'Niedrig', Normal:'Normal', High:'Hoch', Urgent:'Dringend'};
let data = {version:0, todos:[], categories:[], timeEntries:[]};
let tab = 'todos', clockOffset = 0, busy = false, toastTimeout, detailId = null, loaded = false;
let lastStats = null, statsRequest = 0;
const esc = value => String(value ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
const color = value => /^#[0-9a-f]{6}$/i.test(value || '') ? value : '#f97316';
const now = () => Date.now() + clockOffset;
const date = value => value ? new Date(value).toLocaleString('de-DE',{dateStyle:'medium',timeStyle:'short'}) : 'Kein Termin';
const shortDate = value => value ? new Date(value).toLocaleDateString('de-DE',{day:'2-digit',month:'2-digit'}) : '–';
const localInput = value => {
  if (!value) return '';
  const d = new Date(value);
  return new Date(d.getTime() - d.getTimezoneOffset()*60000).toISOString().slice(0,16);
};
const iso = value => value ? new Date(value).toISOString() : null;
const hours = value => Number(value || 0).toLocaleString('de-DE',{maximumFractionDigits:2}) + ' h';
const duration = value => {
  const total = Math.max(0,Math.floor(value || 0));
  return `${Math.floor(total/3600).toString().padStart(2,'0')}:${Math.floor(total%3600/60).toString().padStart(2,'0')}:${(total%60).toString().padStart(2,'0')}`;
};
const entrySeconds = entry => Math.max(0,((entry.endedAt ? new Date(entry.endedAt).getTime() : now()) - new Date(entry.startedAt).getTime()) / 1000);
const taskSeconds = id => data.timeEntries.filter(e=>e.todoId===id).reduce((sum,e)=>sum+entrySeconds(e),0);
const totalSeconds = () => data.timeEntries.reduce((sum,e)=>sum+entrySeconds(e),0);
const categorySeconds = id => data.todos.filter(t=>t.categoryIds.includes(id)).reduce((sum,t)=>sum+taskSeconds(t.id),0);
const task = id => data.todos.find(t=>t.id===id);
const category = id => data.categories.find(c=>c.id===id);
const activeEntry = () => data.timeEntries.find(e=>!e.endedAt);
const isPending = t => ['Open','InProgress'].includes(t.status);
const isOverdue = t => isPending(t) && t.dueAt && new Date(t.dueAt).getTime()<now();
const badge = t => `<span class="badge status-${esc(t.status)}">${esc(statusLabels[t.status])}</span>`;
const categoryTags = t => t.categoryIds.map(id=>category(id)).filter(Boolean).map(c=>`<span class="badge category-tag"><i class="color-dot" style="background:${color(c.color)}"></i>${esc(c.name)}</span>`).join('');
const empty = (title,text) => `<div class="empty-state"><div class="empty-symbol">◇</div><h3>${esc(title)}</h3><p>${esc(text)}</p></div>`;
function toast(text,error=false){
  const el=$('#toast'); el.textContent=text; el.classList.toggle('error',error); el.hidden=false;
  clearTimeout(toastTimeout); toastTimeout=setTimeout(()=>el.hidden=true,error?7500:3500);
}
async function api(path,method='GET',body){
  const headers={};
  if(method!=='GET'){headers['X-FocusFlow']='1';headers['X-Data-Version']=String(data.version);}
  if(body!==undefined) headers['Content-Type']='application/json';
  const response=await fetch('/api'+path,{method,headers,body:body!==undefined?JSON.stringify(body):undefined,cache:'no-store'});
  let payload; try{payload=await response.json();}catch{payload=null;}
  if(!response.ok){
    if(response.status===409) await refresh();
    throw new Error(payload?.title || `Anfrage fehlgeschlagen (${response.status}).`);
  }
  return payload;
}
async function refresh(){
  const start=Date.now(); const result=await api('/state');
  clockOffset=new Date(result.serverNow).getTime()-(start+Date.now())/2;
  const changed=!loaded||data.version!==result.data.version;
  data=result.data;loaded=true;
  $('#connection-status').textContent='Verbunden · Stand '+new Date().toLocaleTimeString('de-DE');
  if(result.recoveryNotice){$('#notice').textContent=result.recoveryNotice;$('#notice').hidden=false;}
  if(changed) render(); else tick();
}
async function mutate(path,method,body){
  if(busy) throw new Error('Eine Änderung wird bereits gespeichert. Bitte kurz warten.');
  busy=true;
  try{data=await api(path,method,body);lastStats=null;render();return data;}
  finally{busy=false;}
}
function fillSelect(el,items,firstLabel,selected='',extra=''){
  el.innerHTML=`<option value="">${esc(firstLabel)}</option>${extra}`+items.map(i=>`<option value="${esc(i.id)}">${esc(i.name ?? i.title)}</option>`).join('');
  el.value=selected;
}
function syncFilterOptions(){
  for(const [selector,items,label,extra] of [
    ['#filter-category',data.categories,'Alle Themen','<option value="none">Ohne Kategorie</option>'],
    ['#filter-parent',data.todos,'Alle','<option value="none">Ohne übergeordnete Aufgabe</option>'],
    ['#filter-related',data.todos,'Alle','']]){
    const el=$(selector),selected=el.value;fillSelect(el,items,label,selected,extra);
  }
}
function renderSummary(){
  const pending=data.todos.filter(isPending),done=data.todos.filter(t=>t.status==='Done').length;
  $('#summary').innerHTML=[
    ['Aktive Aufgaben',pending.length,`${data.todos.filter(t=>t.status==='Open').length} offen · ${data.todos.filter(t=>t.status==='InProgress').length} in Bearbeitung`,''],
    ['Erledigt',done,`${data.todos.length} Aufgaben insgesamt`,''],
    ['Überfällig',data.todos.filter(isOverdue).length,'Aktive Aufgaben mit vergangenem Termin','accent'],
    ['Erfasste Zeit',duration(totalSeconds()),`${hours(data.todos.reduce((s,t)=>s+t.estimatedHours,0))} insgesamt geplant`,'time']
  ].map(([label,value,sub,cls])=>`<div class="kpi ${cls==='accent'?'accent':''}"><div class="kpi-label">${label}</div><div class="kpi-value" ${cls==='time'?'data-live-total':''}>${esc(value)}</div><small>${esc(sub)}</small></div>`).join('');
  $('#nav-count').textContent=pending.length;
}
function filteredTodos(){
  const f=Object.fromEntries(new FormData($('#filters'))), lower=x=>String(x||'').toLocaleLowerCase('de-DE');
  function inDateRange(value,from,to){
    if(!from&&!to)return true;if(!value)return false;
    const ms=new Date(value).getTime();
    return (!from||ms>=new Date(from+'T00:00:00').getTime())&&(!to||ms<new Date(to+'T00:00:00').getTime()+86400000);
  }
  function inRange(value,min,max){return (min===''||value>=Number(min))&&(max===''||value<=Number(max));}
  const result=data.todos.filter(t=>{
    const text=[t.title,t.description,t.resource,...t.categoryIds.map(id=>category(id)?.name||'')].join(' ');
    const spent=taskSeconds(t.id)/3600;
    if(f.q&&!lower(text).includes(lower(f.q)))return false;
    if(f.description&&!lower(t.description).includes(lower(f.description)))return false;
    if(f.resource&&!lower(t.resource).includes(lower(f.resource)))return false;
    if(f.status&&t.status!==f.status)return false;
    if(f.priority&&t.priority!==f.priority)return false;
    if(f.category&&(f.category==='none'?t.categoryIds.length>0:!t.categoryIds.includes(f.category)))return false;
    if(f.parent&&(f.parent==='none'?t.parentTodoId: t.parentTodoId!==f.parent))return false;
    if(f.related&&!t.relatedTodoIds.includes(f.related))return false;
    if(f.dueMode==='overdue'&&!isOverdue(t))return false;
    if(f.dueMode==='none'&&t.dueAt)return false;
    if(f.dueMode==='week'&&(!isPending(t)||!t.dueAt||new Date(t.dueAt).getTime()<now()||new Date(t.dueAt).getTime()>now()+7*86400000))return false;
    const tracked=data.timeEntries.some(e=>e.todoId===t.id),active=activeEntry()?.todoId===t.id;
    if(f.timer==='active'&&!active||f.timer==='tracked'&&!tracked||f.timer==='untracked'&&tracked)return false;
    if(!inDateRange(t.createdAt,f.createdFrom,f.createdTo)||!inDateRange(t.dueAt,f.dueFrom,f.dueTo)
      ||!inDateRange(t.updatedAt,f.updatedFrom,f.updatedTo)||!inDateRange(t.completedAt,f.completedFrom,f.completedTo))return false;
    return inRange(t.estimatedHours,f.estimateMin,f.estimateMax)&&inRange(spent,f.spentMin,f.spentMax);
  });
  const sortValue=t=>{
    switch(f.sort){
      case 'spent':return taskSeconds(t.id);
      case 'priority':return ['Low','Normal','High','Urgent'].indexOf(t.priority);
      case 'status':return ['Open','InProgress','Done','Cancelled'].indexOf(t.status);
      case 'category':return t.categoryIds.map(id=>category(id)?.name||'').sort().join(',');
      case 'parent':return task(t.parentTodoId)?.title||'';
      case 'links':return t.relatedTodoIds.length;
      default:return t[f.sort];
    }
  };
  result.sort((a,b)=>{
    const av=sortValue(a),bv=sortValue(b);
    if(av==null&&bv==null)return a.id.localeCompare(b.id);
    if(av==null)return 1;if(bv==null)return -1;
    const comparison=typeof av==='number'?av-bv:String(av).localeCompare(String(bv),'de',{numeric:true});
    return comparison*(f.direction==='asc'?1:-1)||a.id.localeCompare(b.id);
  });
  return result;
}
function renderTodos(){
  const items=filteredTodos();$('#list-count').textContent=`${items.length} / ${data.todos.length}`;
  $('#todo-list').innerHTML=items.length?items.map(t=>{
    const active=activeEntry()?.todoId===t.id,closed=!isPending(t);
    return `<article class="todo-card ${active?'running':''} ${t.status==='Done'?'is-done':''}">
      <div class="task-main"><button class="task-title" data-action="detail" data-id="${t.id}">${esc(t.title)}</button>
        <p class="task-description">${esc(t.description||'Keine Beschreibung')}</p>
        <div class="tags">${badge(t)}<span class="badge priority-${t.priority}">${priorityLabels[t.priority]}</span>${categoryTags(t)}${t.parentTodoId?`<span class="badge">↳ ${esc(task(t.parentTodoId)?.title||'Übergeordnet')}</span>`:''}${t.relatedTodoIds.length?`<span class="badge">${t.relatedTodoIds.length} Querverweis${t.relatedTodoIds.length===1?'':'e'}</span>`:''}</div></div>
      <div class="task-meta task-due"><strong class="${isOverdue(t)?'overdue':''}">${t.dueAt?esc(date(t.dueAt)):'Ohne Fälligkeit'}</strong>${esc(t.resource||'Keine Ressource')}</div>
      <div class="task-meta task-time"><div class="time-value" data-live-task="${t.id}">${duration(taskSeconds(t.id))}</div>Plan ${hours(t.estimatedHours)}</div>
      <div class="task-actions"><button class="button ${active?'primary':'secondary'} small" data-action="${active?'stop':'start'}" data-id="${t.id}" ${closed?'disabled':''} aria-label="${active?'Timer stoppen':'Timer starten'} für ${esc(t.title)}">${active?'■ Stop':'▶ Start'}</button><button class="button secondary small" data-action="edit" data-id="${t.id}">Bearbeiten</button>${t.status!=='Done'?`<button class="button secondary small" data-action="done" data-id="${t.id}" aria-label="${esc(t.title)} abschließen">✓</button>`:''}</div>
    </article>`;
  }).join(''):empty(data.todos.length?'Keine passenden Aufgaben':'Platz für deinen nächsten Schritt',data.todos.length?'Ändere deine Filter oder lege eine neue Aufgabe an.':'Erstelle deine erste Aufgabe mit „Neue Aufgabe“.');
}
function renderCategories(){
  $('#category-list').innerHTML=data.categories.length?data.categories.map(c=>{
    const tasks=data.todos.filter(t=>t.categoryIds.includes(c.id)),done=tasks.filter(t=>t.status==='Done').length;
    const spent=categorySeconds(c.id)/3600,pct=c.budgetHours?Math.min(100,spent/c.budgetHours*100):0;
    return `<article class="panel category-card" style="--category-color:${color(c.color)}"><h3>${esc(c.name)}</h3><p>${esc(c.description||'Noch keine Beschreibung.')}</p><div class="category-metrics"><div><strong data-live-category="${c.id}">${duration(categorySeconds(c.id))}</strong><small>Ist-Zeit · live</small></div><div><strong>${tasks.length}</strong><small>${done} erledigt · ${tasks.filter(isPending).length} aktiv</small></div></div>
      <div class="task-meta">Budget ${hours(c.budgetHours)} · Plan ${hours(tasks.reduce((s,t)=>s+t.estimatedHours,0))}</div><div class="progress"><span data-budget-category="${c.id}" style="width:${pct}%"></span></div><div class="task-meta" data-budget-label="${c.id}">${c.budgetHours?(spent>c.budgetHours?'Budget überschritten':hours(Math.max(0,c.budgetHours-spent))+' Budget übrig'):'Kein Zeitbudget festgelegt'}</div><div class="task-meta" style="margin-top:10px">${esc(c.resource||'Keine Ressource')}<br>${c.targetDate?'Ziel: '+esc(date(c.targetDate)):'Kein Zieltermin'}</div>
      <div class="category-actions"><button class="button secondary small" data-action="category-filter" data-id="${c.id}">Aufgaben ansehen</button><button class="button secondary small" data-action="category-edit" data-id="${c.id}">Bearbeiten</button><button class="button danger small" data-action="category-delete" data-id="${c.id}">Löschen</button></div></article>`;
  }).join(''):empty('Themen schaffen Übersicht','Lege eine Kategorie mit Farbe, Zeitbudget und Zieltermin an.');
}
function barChart(items,{daily=false}={}){
  const width=650,height=215,left=35,bottom=36,top=28,plot=height-bottom-top;
  const max=Math.max(1,...items.map(i=>i.value)),step=(width-left-15)/Math.max(1,items.length);
  return `<svg class="chart" viewBox="0 0 ${width} ${height}" role="img" aria-label="${daily?'Zeiterfassung der letzten 14 Tage in Stunden':'Vergleich von Aufwand in Stunden'}">
    ${[0,.5,1].map(v=>`<line x1="${left}" y1="${top+plot*(1-v)}" x2="${width-10}" y2="${top+plot*(1-v)}" stroke="#edf0f5"/><text x="0" y="${top+plot*(1-v)+4}">${(max*v).toLocaleString('de-DE',{maximumFractionDigits:1})}</text>`).join('')}
    ${items.map((i,index)=>{const x=left+index*step+step*.18,h=i.value/max*plot;return `<g><title>${esc(i.label)}: ${esc(hours(i.value))}</title><rect x="${x}" y="${top+plot-h}" width="${step*.64}" height="${Math.max(0,h)}" rx="3" fill="${color(i.color)}"/><text text-anchor="middle" x="${x+step*.32}" y="${height-13}">${esc(i.label.length>11?i.label.slice(0,10)+'…':i.label)}</text>${items.length<9?`<text class="value-label" text-anchor="middle" x="${x+step*.32}" y="${top+plot-h-8}">${Number(i.value).toLocaleString('de-DE',{maximumFractionDigits:1})}</text>`:''}</g>`;}).join('')}
  </svg>`;
}
function donut(s){
  const values=[s.open,s.inProgress,s.done,s.cancelled],colors=['#94a3b8','#f97316','#22c55e','#b09dbb'],labels=['Offen','In Bearbeitung','Erledigt','Abgebrochen'];
  let offset=0;const circumference=2*Math.PI*65;
  const rings=values.map((value,i)=>{const length=s.total?value/s.total*circumference:0,svg=`<circle cx="90" cy="90" r="65" fill="none" stroke="${colors[i]}" stroke-width="19" stroke-dasharray="${length} ${circumference-length}" stroke-dashoffset="${-offset}" transform="rotate(-90 90 90)"/>`;offset+=length;return svg;}).join('');
  return `<div class="donut-layout"><svg class="donut" viewBox="0 0 180 180" role="img" aria-label="${esc(values.map((v,i)=>labels[i]+': '+v).join(', '))}"><circle cx="90" cy="90" r="65" fill="none" stroke="#f0f2f6" stroke-width="19"/>${rings}<text x="90" y="89">${s.total}</text><text x="90" y="109" class="donut-sub">AUFGABEN</text></svg><ul class="stat-list" style="flex:1">${values.map((v,i)=>`<li><span><i class="color-dot" style="background:${colors[i]}"></i> ${labels[i]}</span><b>${v}</b></li>`).join('')}</ul></div>`;
}
function taskStatList(items,dateKey){
  return items.length?`<ul class="stat-list">${items.map(t=>`<li><button class="task-title" data-action="detail" data-id="${t.id}">${esc(t.title)}</button><time>${esc(date(t[dateKey]))}</time></li>`).join('')}</ul>`:'<p class="helper">Hier gibt es derzeit keine Aufgaben.</p>';
}
function renderStatistics(s){
  const completion=s.total?Math.round(s.done/s.total*100):0;
  $('#statistics-content').innerHTML=`<div class="stats-grid">
    <div class="panel narrative full"><h2>Dein Fortschritt auf einen Blick</h2><p>Du hast <strong>${s.total} Aufgaben</strong> angelegt. Davon sind <strong>${s.done} erledigt (${completion} %)</strong>, ${s.inProgress} in Bearbeitung, ${s.open} offen und ${s.cancelled} abgebrochen. ${s.overdue.length?`<strong class="overdue">${s.overdue.length} aktive Aufgaben sind überfällig.</strong>`:'Aktuell ist keine aktive Aufgabe überfällig.'}</p><p>Insgesamt wurden <strong data-live-total>${duration(totalSeconds())}</strong> erfasst, bei ${hours(s.estimatedHours)} geplantem Aufwand. Aktive Aufgaben haben ${hours(s.pendingEstimatedHours)} ursprünglichen Planaufwand und rechnerisch ${hours(s.remainingEstimatedHours)} verbleibenden Planaufwand. Das ist eine Schätzung, keine automatische Prognose.</p><p>In den nächsten sieben Tagen stehen ${s.upcoming.length} Aufgaben an. ${s.withoutDueDate} aktive Aufgaben haben noch keinen Termin. Ressourcen werden anhand des Feldes „Ressource“ der Aufgaben ausgewertet, nicht anhand der Kategorie.</p></div>
    <div class="panel"><h2>Aufgabenstatus</h2>${donut(s)}</div>
    <div class="panel"><h2>Plan und erfasster Aufwand</h2>${barChart([{label:'Plan gesamt',value:s.estimatedHours,color:'#cbd5e1'},{label:'Ist gesamt',value:s.totalSeconds/3600,color:'#f97316'},{label:'Rest aktiv',value:s.remainingEstimatedHours,color:'#93c5fd'}])}<p class="helper">Alle Werte in Stunden. Rest = Summe max. 0 aus Plan minus Ist je aktiver Aufgabe.</p></div>
    <div class="panel full"><h2>Dein Zeitverlauf · letzte 14 Tage</h2>${barChart(s.days.map(d=>({label:d.date.slice(8)+'.'+d.date.slice(5,7),value:d.seconds/3600,color:'#f97316'})),{daily:true})}<p class="helper">Tagesgrenzen in UTC. Sitzungen über Mitternacht werden auf die jeweiligen Tage aufgeteilt. Heute bisher: ${duration(s.todaySeconds)}.</p></div>
    <div class="panel"><h2>Überfällig <span class="badge priority-Urgent">${s.overdue.length}</span></h2>${taskStatList(s.overdue,'dueAt')}</div>
    <div class="panel"><h2>In den nächsten 7 Tagen</h2>${taskStatList(s.upcoming,'dueAt')}</div>
    <div class="panel full"><h2>Zeit & Budget nach Thema</h2><p class="helper">Mehrfachzugeordnete Aufgaben erscheinen in jedem Thema vollständig. Themenzeiten daher nicht zur globalen Zeit addieren. Plan und Ist berücksichtigen alle Status.</p>${s.categories.length?`<div class="table-scroll"><table><thead><tr><th>Thema</th><th>Aufgaben</th><th>Plan</th><th>Ist · live</th><th>Budget</th><th>Ressource</th><th>Zieltermin</th></tr></thead><tbody>${s.categories.map(c=>`<tr><td><i class="color-dot" style="background:${color(c.color)}"></i> ${esc(c.name)}</td><td>${c.done} / ${c.count} erledigt</td><td>${hours(c.estimatedHours)}</td><td data-live-category="${c.id}">${duration(categorySeconds(c.id))}</td><td>${hours(c.budgetHours)}</td><td>${esc(c.resource||'–')}</td><td>${esc(date(c.targetDate))}</td></tr>`).join('')}</tbody></table></div>`:'<p class="helper">Noch keine Kategorien angelegt.</p>'}</div>
    <div class="panel full"><h2>Ressourcenübersicht</h2>${s.resources.length?`<div class="table-scroll"><table><thead><tr><th>Ressource</th><th>Aufgaben</th><th>Davon aktiv</th><th>Planaufwand</th><th>Ist-Aufwand</th></tr></thead><tbody>${s.resources.map(r=>`<tr><td>${esc(r.name)}</td><td>${r.count}</td><td>${r.pending}</td><td>${hours(r.estimatedHours)}</td><td>${duration(r.seconds)}</td></tr>`).join('')}</tbody></table></div>`:'<p class="helper">Noch keine Aufgaben angelegt.</p>'}</div>
    <div class="panel"><h2>Zeitintensivste Aufgaben</h2>${s.topTasks.length?`<ul class="stat-list">${s.topTasks.map(t=>`<li><button class="task-title" data-action="detail" data-id="${t.id}">${esc(t.title)}</button><b data-live-task="${t.id}">${duration(taskSeconds(t.id))}</b></li>`).join('')}</ul>`:'<p class="helper">Noch keine Aufgaben.</p>'}</div>
    <div class="panel"><h2>Zuletzt abgeschlossen</h2>${taskStatList(s.completedRecent,'completedAt')}</div>
  </div><p class="helper">Die Statistik bezieht sich auf alle Aufgaben, unabhängig von Listenfiltern. Live-Zeiten aktualisieren sich jede Sekunde, Diagramme und Berechnungen alle 15 Sekunden.</p>`;
}
async function loadStatistics(){
  const request=++statsRequest;
  try{
    const result=await api('/statistics');
    if(request!==statsRequest||tab!=='statistics')return;
    if(result.version!==data.version)return;
    lastStats=result;renderStatistics(result);
  }catch(error){if(tab==='statistics')toast(error.message,true);}
}
function render(){
  syncFilterOptions();renderSummary();renderTodos();renderCategories();renderTimer();
  if(tab==='statistics')loadStatistics();
  if(detailId&&$('#detail-dialog').open)renderDetail(detailId);
  tick();
}
function switchTab(value){
  if(!['todos','categories','statistics'].includes(value))value='todos';tab=value;
  $$('.nav').forEach(el=>el.classList.toggle('active',el.dataset.tab===tab));
  for(const name of ['todos','categories','statistics'])$('#'+name+'-tab').hidden=name!==tab;
  const titles={todos:['Aufgaben','Plane, verknüpfe und erfasse deinen Fortschritt.','+ Neue Aufgabe'],categories:['Themen & Kategorien','Bündele deine Arbeit und behalte Zeitbudgets im Blick.','+ Neue Kategorie'],statistics:['Statistiken','Aus Aufgaben und Zeiten werden klare Erkenntnisse.','+ Neue Aufgabe']};
  $('#page-title').textContent=titles[tab][0];$('#page-subtitle').textContent=titles[tab][1];$('#new-button').textContent=titles[tab][2];
  history.replaceState(null,'','#'+tab);
  if(tab==='statistics'){if(lastStats)renderStatistics(lastStats);else $('#statistics-content').innerHTML='<div class="panel">Statistik wird geladen …</div>';loadStatistics();}
}
function renderTimer(){
  const e=activeEntry(),bar=$('#timer-bar');bar.hidden=!e;if(!e)return;
  const t=task(e.todoId);if(!t)return;
  bar.innerHTML=`<div class="timer-pulse"></div><div class="timer-info"><small>ZEITERFASSUNG LÄUFT</small><strong>${esc(t.title)}</strong></div><div class="timer-category"><small>${esc(t.categoryIds.map(id=>category(id)?.name).filter(Boolean).join(' · ')||'Ohne Thema')}</small><div class="time-value" data-live-task="${t.id}">${duration(taskSeconds(t.id))}</div></div><span class="timer-number" data-live-session>${duration(entrySeconds(e))}</span><button class="button secondary" data-action="stop" data-id="${t.id}">■ Stoppen</button>`;
}
function tick(){
  $$('[data-live-total]').forEach(el=>el.textContent=duration(totalSeconds()));
  $$('[data-live-task]').forEach(el=>el.textContent=duration(taskSeconds(el.dataset.liveTask)));
  $$('[data-live-category]').forEach(el=>el.textContent=duration(categorySeconds(el.dataset.liveCategory)));
  const e=activeEntry();$$('[data-live-session]').forEach(el=>el.textContent=e?duration(entrySeconds(e)):'00:00:00');
  $$('[data-budget-category]').forEach(el=>{const c=category(el.dataset.budgetCategory);if(c)el.style.width=(c.budgetHours?Math.min(100,categorySeconds(c.id)/3600/c.budgetHours*100):0)+'%';});
  $$('[data-budget-label]').forEach(el=>{const c=category(el.dataset.budgetLabel);if(!c)return;const spent=categorySeconds(c.id)/3600;el.textContent=c.budgetHours?(spent>c.budgetHours?'Budget überschritten':hours(Math.max(0,c.budgetHours-spent))+' Budget übrig'):'Kein Zeitbudget festgelegt';});
}
function openTodo(id){
  if(!loaded){toast('Die App ist noch nicht verbunden.',true);return;}
  const t=id?task(id):null;if(id&&!t)return;
  const form=$('#todo-form');form.reset();$('#todo-error').hidden=true;
  $('#todo-dialog-title').textContent=t?'Aufgabe bearbeiten':'Neue Aufgabe';
  const values=t||{id:'',title:'',description:'',createdAt:new Date(now()).toISOString(),dueAt:null,status:'Open',priority:'Normal',estimatedHours:0,resource:'',categoryIds:[],relatedTodoIds:[],parentTodoId:null};
  for(const key of ['id','title','description','status','priority','estimatedHours','resource'])form.elements[key].value=values[key]??'';
  for(const key of ['createdAt','dueAt'])form.elements[key].value=localInput(values[key]);
  fillSelect(form.elements.parentTodoId,data.todos.filter(x=>x.id!==id),'Keine',values.parentTodoId||'');
  for(const [key,items] of [['categoryIds',data.categories],['relatedTodoIds',data.todos.filter(x=>x.id!==id)]]){
    form.elements[key].innerHTML=items.map(i=>`<option value="${i.id}" ${values[key].includes(i.id)?'selected':''}>${esc(i.name??i.title)}</option>`).join('');
  }
  $('#todo-dialog').showModal();
}
function openCategory(id){
  if(!loaded){toast('Die App ist noch nicht verbunden.',true);return;}
  const c=id?category(id):null;if(id&&!c)return;
  const form=$('#category-form');form.reset();$('#category-error').hidden=true;
  $('#category-dialog-title').textContent=c?'Kategorie bearbeiten':'Neue Kategorie';
  const values=c||{id:'',name:'',description:'',color:'#f97316',budgetHours:0,resource:'',targetDate:null};
  for(const key of ['id','name','description','color','budgetHours','resource'])form.elements[key].value=values[key];
  form.elements.targetDate.value=localInput(values.targetDate);$('#category-dialog').showModal();
}
function renderDetail(id){
  const t=task(id);if(!t){$('#detail-dialog').close();detailId=null;return;}
  const entries=data.timeEntries.filter(e=>e.todoId===id).sort((a,b)=>b.startedAt.localeCompare(a.startedAt));
  const children=data.todos.filter(other=>other.parentTodoId===id);
  const outgoing=t.relatedTodoIds.map(task).filter(Boolean),incoming=data.todos.filter(other=>other.relatedTodoIds.includes(id));
  const related=[...new Map([...outgoing,...incoming].map(other=>[other.id,other])).values()];
  const link=x=>`<button class="button secondary small" data-action="detail" data-id="${x.id}">${esc(x.title)}</button>`;
  $('#detail-content').innerHTML=`<div class="dialog-heading"><div><div class="eyebrow">AUFGABENDETAILS</div><h2>${esc(t.title)}</h2></div><button class="icon-button" data-close="detail-dialog" aria-label="Schließen">×</button></div><div class="tags">${badge(t)}<span class="badge priority-${t.priority}">${priorityLabels[t.priority]}</span>${categoryTags(t)}</div><div class="detail-description">${esc(t.description||'Keine Beschreibung hinterlegt.')}</div>
    <div class="detail-grid"><div><strong>Erstellt</strong>${esc(date(t.createdAt))}</div><div><strong>Letzte Änderung</strong>${esc(date(t.updatedAt))}</div><div><strong>Fällig</strong><span class="${isOverdue(t)?'overdue':''}">${esc(date(t.dueAt))}</span></div><div><strong>Abgeschlossen</strong>${t.completedAt?esc(date(t.completedAt)):'Noch nicht abgeschlossen'}</div><div><strong>Planaufwand</strong>${hours(t.estimatedHours)}</div><div><strong>Ist-Aufwand</strong><span data-live-task="${id}">${duration(taskSeconds(id))}</span></div><div><strong>Ressource</strong>${esc(t.resource||'Nicht zugeordnet')}</div><div><strong>Übergeordnete Aufgabe</strong>${t.parentTodoId&&task(t.parentTodoId)?link(task(t.parentTodoId)):'Keine'}</div></div>
    ${children.length?`<h3>Unteraufgaben</h3><div class="detail-links">${children.map(link).join('')}</div>`:''}
    ${related.length?`<h3>Querverweise · ein- und ausgehend</h3><div class="detail-links">${related.map(link).join('')}</div>`:''}
    <div class="section-heading"><h3>Zeitbuchungen (${entries.length})</h3><button class="button secondary small" data-action="manual" data-id="${id}">+ Zeit nachtragen</button></div>
    ${entries.length?`<div class="table-scroll"><table><thead><tr><th>Beginn / Ende</th><th>Dauer</th><th>Typ / Notiz</th><th></th></tr></thead><tbody>${entries.map(e=>`<tr><td>${esc(date(e.startedAt))}<br><span class="muted">${e.endedAt?esc(date(e.endedAt)):'Läuft …'}</span></td><td ${!e.endedAt?'data-live-session':''}>${duration(entrySeconds(e))}</td><td>${e.isManual?'Manuell':'Timer'}${e.note?`<div class="entry-note">${esc(e.note)}</div>`:''}</td><td>${e.endedAt?`<button class="button danger small" data-action="time-delete" data-id="${e.id}">Löschen</button>`:''}</td></tr>`).join('')}</tbody></table></div>`:'<p class="helper">Noch keine Zeit erfasst. Starte den Timer oder trage Zeit nach.</p>'}
    <div class="dialog-actions"><button class="button danger" data-action="delete" data-id="${id}">Löschen</button><button class="button secondary" data-action="edit" data-id="${id}">Bearbeiten</button>${isPending(t)?`<button class="button primary" data-action="${activeEntry()?.todoId===id?'stop':'start'}" data-id="${id}">${activeEntry()?.todoId===id?'■ Stoppen':'▶ Timer starten'}</button>`:`<button class="button primary" data-action="reopen" data-id="${id}">Wieder öffnen</button>`}</div>`;
}
function openDetail(id){detailId=id;renderDetail(id);if(task(id)&&!$('#detail-dialog').open)$('#detail-dialog').showModal();}
function openTime(id){
  const form=$('#time-form');form.reset();form.elements.todoId.value=id;
  form.elements.startedAt.value=localInput(new Date(now()-31*60000).toISOString());
  $('#time-error').hidden=true;$('#time-dialog').showModal();
}
async function handleAction(button){
  const id=button.dataset.id;
  switch(button.dataset.action){
    case 'detail':openDetail(id);break;
    case 'edit':$('#detail-dialog').close();detailId=null;openTodo(id);break;
    case 'start':await mutate(`/todos/${id}/timer/start`,'POST');toast('Timer gestartet. Ein vorheriger Timer wurde automatisch gestoppt.');break;
    case 'stop':await mutate(`/todos/${id}/timer/stop`,'POST');toast('Zeit gespeichert.');break;
    case 'done':await mutate(`/todos/${id}/status`,'PATCH',{status:'Done'});toast('Aufgabe abgeschlossen.');break;
    case 'reopen':await mutate(`/todos/${id}/status`,'PATCH',{status:'Open'});toast('Aufgabe wieder geöffnet.');break;
    case 'delete':if(confirm('Aufgabe und ihre Zeitbuchungen unwiderruflich löschen? Unteraufgaben bleiben erhalten, Verknüpfungen werden entfernt.')){await mutate(`/todos/${id}`,'DELETE');toast('Aufgabe gelöscht.');}break;
    case 'manual':openTime(id);break;
    case 'time-delete':if(confirm('Diese Zeitbuchung unwiderruflich löschen?')){await mutate(`/time/${id}`,'DELETE');toast('Zeitbuchung gelöscht.');}break;
    case 'category-edit':openCategory(id);break;
    case 'category-delete':if(confirm('Kategorie löschen? Aufgaben und Zeitbuchungen bleiben erhalten, nur die Zuordnung wird entfernt.')){await mutate(`/categories/${id}`,'DELETE');toast('Kategorie gelöscht.');}break;
    case 'category-filter':$('#filters').reset();$('#filter-category').value=id;switchTab('todos');renderTodos();break;
  }
}
document.addEventListener('click',async event=>{
  const close=event.target.closest('[data-close]');if(close){$('#'+close.dataset.close).close();if(close.dataset.close==='detail-dialog')detailId=null;return;}
  const nav=event.target.closest('[data-tab]');if(nav){switchTab(nav.dataset.tab);return;}
  const action=event.target.closest('[data-action]');if(action&&!action.disabled){try{await handleAction(action);}catch(error){toast(error.message,true);}}
});
$('#new-button').addEventListener('click',()=>tab==='categories'?openCategory():openTodo());
$('.brand').addEventListener('click',event=>{event.preventDefault();switchTab('todos');});
$('#filters').addEventListener('submit',event=>event.preventDefault());
$('#filters').addEventListener('input',renderTodos);
$('#filters').addEventListener('change',renderTodos);
$('#reset-filters').addEventListener('click',()=>{$('#filters').reset();renderTodos();});
$('#detail-dialog').addEventListener('close',()=>detailId=null);
for(const [formId,errorId,save] of [
  ['todo-form','todo-error',async form=>{
    const f=Object.fromEntries(new FormData(form));
    const payload={title:f.title,description:f.description,createdAt:iso(f.createdAt),dueAt:iso(f.dueAt),status:f.status,priority:f.priority,estimatedHours:Number(f.estimatedHours),resource:f.resource,parentTodoId:f.parentTodoId||null,
      categoryIds:[...form.elements.categoryIds.selectedOptions].map(o=>o.value),relatedTodoIds:[...form.elements.relatedTodoIds.selectedOptions].map(o=>o.value)};
    await mutate(f.id?`/todos/${f.id}`:'/todos',f.id?'PUT':'POST',payload);$('#todo-dialog').close();toast('Aufgabe gespeichert.');
  }],
  ['category-form','category-error',async form=>{
    const f=Object.fromEntries(new FormData(form));
    await mutate(f.id?`/categories/${f.id}`:'/categories',f.id?'PUT':'POST',{name:f.name,description:f.description,color:f.color,resource:f.resource,budgetHours:Number(f.budgetHours),targetDate:iso(f.targetDate)});
    $('#category-dialog').close();toast('Kategorie gespeichert.');
  }],
  ['time-form','time-error',async form=>{
    const f=Object.fromEntries(new FormData(form));
    await mutate(`/todos/${f.todoId}/time`,'POST',{startedAt:iso(f.startedAt),minutes:Number(f.minutes),note:f.note});$('#time-dialog').close();toast('Zeit nachgetragen.');
  }]
]){
  $('#'+formId).addEventListener('submit',async event=>{
    event.preventDefault();const form=event.currentTarget,button=$('[type="submit"]',form),error=$('#'+errorId);
    if(button.disabled)return;error.hidden=true;button.disabled=true;
    try{await save(form);}catch(ex){error.textContent=ex.message;error.hidden=false;}
    finally{button.disabled=false;}
  });
}
setInterval(tick,1000);
setInterval(()=>{if(!busy&&!document.hidden)refresh().catch(error=>{$('#connection-status').textContent='Nicht verbunden';toast('Verbindung zum lokalen Server unterbrochen. '+error.message,true);});},15000);
setInterval(()=>{if(tab==='statistics'&&!document.hidden)loadStatistics();},15000);
// Update time-dependent filters and deadline indicators without disturbing an open form.
setInterval(()=>{if(loaded&&!document.hidden){renderSummary();if(tab==='todos')renderTodos();}},60000);
document.addEventListener('visibilitychange',()=>{if(!document.hidden&&!busy)refresh().catch(error=>toast(error.message,true));});
switchTab(location.hash.slice(1));
refresh().catch(error=>{
  $('#connection-status').textContent='Nicht verbunden';$('#notice').textContent='Die App konnte nicht geladen werden. Läuft der lokale C# Server? '+error.message;$('#notice').hidden=false;
  $('#todo-list').innerHTML=empty('Keine Verbindung','Starte das Projekt und lade diese Seite neu.');
});
