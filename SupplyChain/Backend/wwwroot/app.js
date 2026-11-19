const labels={planned:"Planificado",picked_up:"Recogido",in_transit:"En tránsito",delayed:"Demorado",out_for_delivery:"En reparto",delivered:"Entregado",exception:"Excepción",cancelled:"Cancelado"};
const choices={planned:["picked_up","delayed","cancelled"],picked_up:["in_transit","delayed","exception"],in_transit:["out_for_delivery","delayed","exception"],delayed:["in_transit","out_for_delivery","exception"],out_for_delivery:["delivered","exception"],exception:["in_transit","delayed","delivered"],delivered:[],cancelled:[]};
const $=selector=>document.querySelector(selector);
async function api(path,options={}){const response=await fetch(path,{...options,headers:{"Content-Type":"application/json",...(options.headers||{})}});if(!response.ok){const body=await response.json().catch(()=>({}));throw new Error(body.error||"No se pudo completar la operación.");}return response.status===204?null:response.json();}
function localDate(value){return new Date(value).toLocaleString("es-BR",{dateStyle:"short",timeStyle:"short",timeZone:"UTC"})+" UTC";}
function render(shipments,scores){
 const host=$("#shipments");host.replaceChildren();const scoreMap=new Map(scores.map(score=>[score.shipmentId,score]));
 $("#moving").textContent=shipments.filter(item=>["picked_up","in_transit","delayed","out_for_delivery"].includes(item.status)).length;
 $("#risk").textContent=scores.filter(score=>score.riskScore>=50).length;
 $("#evaluated").textContent=scores.length?localDate(scores[0].evaluatedAt):"Pendiente";
 if(!shipments.length){host.innerHTML='<p class="empty">Aún no hay envíos. Registra uno para empezar el seguimiento.</p>';return;}
 for(const item of shipments){
  const card=document.createElement("article");card.className="shipment";
  const top=document.createElement("div");top.className="shipment-top";
  const ref=document.createElement("h3");ref.textContent=item.orderReference+" · "+item.carrier;
  const badge=document.createElement("span");badge.className="status "+item.status;badge.textContent=labels[item.status]||item.status;top.append(ref,badge);
  const route=document.createElement("p");route.className="route";route.textContent=item.origin+" → "+item.destination;
  const eta=document.createElement("p");eta.className="eta";eta.textContent="ETA planificada: "+localDate(item.plannedArrival);
  const score=scoreMap.get(item.id);const scoreLine=document.createElement("p");scoreLine.className="score";
  scoreLine.textContent=score?"Riesgo ETA "+score.riskScore+"/100 · demora estimada "+score.estimatedDelayMinutes+" min · confianza "+score.confidence+"%":"Esperando evaluación Python";
  card.append(top,route,eta,scoreLine);
  if(choices[item.status].length){
   const controls=document.createElement("div");controls.className="controls";
   const select=document.createElement("select");for(const status of choices[item.status]){const option=document.createElement("option");option.value=status;option.textContent=labels[status];select.append(option);}
   const button=document.createElement("button");button.textContent="Registrar evento";button.addEventListener("click",async()=>{try{await api("/api/shipments/"+item.id+"/events",{method:"POST",body:JSON.stringify({status:select.value,location:item.destination,note:"Actualización desde el panel",occurredAt:new Date().toISOString(),expectedVersion:item.version})});await refresh();}catch(error){$("#notice").textContent=error.message;}});
   controls.append(select,button);card.append(controls);
  }
  host.append(card);
 }
}
async function refresh(){try{const [shipments,scores]=await Promise.all([api("/api/shipments"),api("/api/eta-scores")]);render(shipments,scores);}catch(error){$("#notice").textContent=error.message;}}
$("#shipment-form").addEventListener("submit",async event=>{event.preventDefault();const data=Object.fromEntries(new FormData(event.currentTarget));data.plannedArrival=new Date(data.plannedArrival).toISOString();try{await api("/api/shipments",{method:"POST",body:JSON.stringify(data)});event.currentTarget.reset();$("#notice").textContent="Envío registrado.";await refresh();}catch(error){$("#notice").textContent=error.message;}});
$("#refresh").addEventListener("click",refresh);refresh();setInterval(refresh,30000);