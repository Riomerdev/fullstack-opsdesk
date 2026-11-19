const labels={new:"Nuevo",in_progress:"En curso",resolved:"Resuelto"};
const next={new:["in_progress","Tomar"],in_progress:["resolved","Resolver"],resolved:["new","Reabrir"]};
const $=selector=>document.querySelector(selector);
const safeDate=value=>new Date(value).toLocaleString("es-BR",{dateStyle:"short",timeStyle:"short"});
async function api(path,options={}){const response=await fetch(path,{...options,headers:{"Content-Type":"application/json",...(options.headers||{})}});if(!response.ok){const body=await response.json().catch(()=>({}));throw new Error(body.error||"La solicitud no pudo completarse.");}return response.status===204?null:response.json();}
function render(tickets){
 const host=$("#tickets");host.replaceChildren();
 $("#open-count").textContent=tickets.filter(t=>t.status!=="resolved").length;
 const now=Date.now();$("#overdue-count").textContent=tickets.filter(t=>t.status!=="resolved"&&new Date(t.dueAt).getTime()<now).length;
 if(!tickets.length){host.innerHTML='<p class="empty">No hay tickets para este filtro. Registra uno para iniciar la cola.</p>';return;}
 for(const ticket of tickets){
   const card=document.createElement("article");card.className="ticket";
   const info=document.createElement("div");info.className="ticket-info";
   const title=document.createElement("h3");title.textContent=ticket.title;
   const detail=document.createElement("p");detail.textContent=ticket.category+" · "+ticket.requester;
   const meta=document.createElement("div");meta.className="ticket-meta";
   const priority=document.createElement("span");priority.className="priority "+ticket.priority;priority.textContent=ticket.priority.toUpperCase();
   const state=document.createElement("span");state.className="state";state.textContent=labels[ticket.status]||ticket.status;
   const deadline=document.createElement("span");deadline.textContent="Vence "+safeDate(ticket.dueAt);
   meta.append(priority,state,deadline);info.append(title,detail,meta);card.append(info);
   const action=document.createElement("button");action.className="advance";action.textContent=next[ticket.status][1];
   action.addEventListener("click",async()=>{try{await api("/api/tickets/"+ticket.id+"/status",{method:"POST",body:JSON.stringify({status:next[ticket.status][0],expectedVersion:ticket.version})});await refresh();}catch(error){$("#notice").textContent=error.message;}});
   card.append(action);host.append(card);
 }
}
async function refresh(){
 try{
  const status=$("#status-filter").value;const tickets=await api("/api/tickets"+(status?"?status="+encodeURIComponent(status):""));
  render(tickets);
  const metric=await api("/api/metrics/latest");
  $("#metric-time").textContent=metric?safeDate(metric.generatedAt):"Esperando worker";
  if(metric){$("#open-count").textContent=metric.open;$("#overdue-count").textContent=metric.overdue;}
 }catch(error){$("#notice").textContent=error.message;}
}
$("#ticket-form").addEventListener("submit",async event=>{event.preventDefault();const data=Object.fromEntries(new FormData(event.currentTarget));try{await api("/api/tickets",{method:"POST",body:JSON.stringify(data)});event.currentTarget.reset();$("#notice").textContent="Ticket registrado.";await refresh();}catch(error){$("#notice").textContent=error.message;}});
$("#refresh").addEventListener("click",refresh);$("#status-filter").addEventListener("change",refresh);
refresh();setInterval(refresh,30000);