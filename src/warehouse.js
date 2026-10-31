const specs=[
 {key:'c4',label:'С4',aliases:['С4','C4'],damage:400},
 {key:'rpg',label:'РПГ',aliases:['РПГ'],damage:200},
 {key:'grenade',label:'ГРАНАТА',aliases:['ГРАНАТА','Граната'],damage:100},
 {key:'dynamite',label:'ДИНАМИТ',aliases:['ДИНАМИТ'],damage:200},
 {key:'pipe',label:'ПАЙПА',aliases:['ПАЙПА'],damage:0},
 {key:'40mm',label:'40 мм',aliases:['40'],damage:0}
];
const row=(...components)=>({type:1,components});
const reply=(content,components=[])=>({type:4,data:{flags:64,content,components,allowed_mentions:{parse:[]}}});
const validId=s=>/^[a-zA-Z0-9-]{1,40}$/.test(String(s));
const parseChoice=value=>{
 const [key,quantity,squadId,...extra]=String(value||'').split(':');
 const spec=specs.find(x=>x.key===key);
 if(!spec||extra.length||!/^\d{1,7}$/.test(quantity)||!validId(squadId))return null;
 const expected=Number(quantity);if(!Number.isSafeInteger(expected)||expected<0||expected>1000000)return null;
 return {spec,expected,squadId};
};
function inventoryRows(items){
 return specs.map(spec=>{const matches=items.filter(x=>spec.aliases.includes(x.item));return {...spec,row:matches.length===1?matches[0]:null,ambiguous:matches.length>1,quantity:matches.length===1?Number(matches[0].quantity):0}});
}
export async function warehousePanel(db,member,homepage,notice=''){
 const items=await db('explosives_inventory',{query:'?squad_id=eq.'+encodeURIComponent(member.squad_id)+'&select=item,quantity'});
 const stock=inventoryRows(items),walls=Math.floor(stock.reduce((sum,x)=>sum+x.quantity*x.damage,0)/1200);
 const choices=stock.filter(x=>x.row&&Number.isSafeInteger(x.quantity)&&x.quantity>=0&&x.quantity<=1000000).map(x=>({label:x.label,value:x.key+':'+x.quantity+':'+member.squad_id,description:'Сейчас '+x.quantity+' шт. · задать новое количество'}));
 const components=[];
 if(choices.length)components.push(row({type:3,custom_id:'fury:stock:choose',placeholder:'Изменить количество взрывчатки',min_values:1,max_values:1,options:choices}));
 components.push(row({type:2,style:2,label:'Обновить запасы',custom_id:'fury:explosives'},{type:2,style:5,label:'Открыть склад на сайте ↗',url:homepage.replace(/\/$/,'')+'/?section=explosives'}));
 const wallWord=n=>{const a=n%100,b=n%10;return a>=11&&a<=14?'стен':b===1?'стену':b>=2&&b<=4?'стены':'стен'};
 return reply((notice?notice+'\n\n':'')+'**СКЛАД · ВЗРЫВЧАТКА**\n'+stock.map(x=>'**'+x.label+':** '+(x.row?x.quantity+' шт.':x.ambiguous?'нужно проверить дубли':'нет записи')).join('\n')+'\n\nМожно взорвать **'+walls+' '+wallWord(walls)+'**.\nВыбери тип ниже и введи новое общее количество.',components);
}
export function warehouseModal(value){
 const choice=parseChoice(value);if(!choice)return reply('Некорректная позиция склада. Открой склад заново.');
 const {spec,expected,squadId}=choice;
 return {type:9,data:{custom_id:'fury:stock:save:'+spec.key+':'+expected+':'+squadId,title:'Склад · '+spec.label,components:[row({type:4,custom_id:'quantity',label:'Новое общее количество (0–300)',style:1,value:String(expected),min_length:1,max_length:3,required:true})]}};
}
export async function saveWarehouse(interaction,{db,linked,homepage,testMode}){
 if(testMode)return reply('Тестовый режим: изменение склада отключено.');
 const member=await linked(interaction);if(!member)return reply('Сначала привяжи Discord к SCUM HUB.');
 const choice=parseChoice(String(interaction.data?.custom_id||'').slice('fury:stock:save:'.length));
 if(!choice||String(choice.squadId)!==String(member.squad_id))return reply('Отряд изменился или форма устарела. Открой склад заново.');
 const input=interaction.data?.components?.flatMap(x=>x.components||[]).find(x=>x.custom_id==='quantity');
 const raw=String(input?.value??'').trim();
 if(!/^\d{1,3}$/.test(raw)||Number(raw)>300)return reply('Введите целое количество от 0 до 300.');
 const items=await db('explosives_inventory',{query:'?squad_id=eq.'+encodeURIComponent(member.squad_id)+'&select=item,quantity'});
 const stock=inventoryRows(items).find(x=>x.key===choice.spec.key);
 if(!stock?.row)return reply('Позиция не найдена или есть дубли. Обнови склад.');
 if(stock.quantity!==choice.expected)return warehousePanel(db,member,homepage,'Запасы уже изменил другой участник. Ничего не перезаписано — выбери позицию ещё раз.');
 const quantity=Number(raw);
 const updated=await db('explosives_inventory',{method:'PATCH',query:'?squad_id=eq.'+encodeURIComponent(member.squad_id)+'&item=eq.'+encodeURIComponent(stock.row.item)+'&quantity=eq.'+choice.expected,body:{quantity,updated_at:new Date().toISOString(),updated_by:member.id}});
 if(updated.length!==1)return warehousePanel(db,member,homepage,'Запасы изменились во время сохранения. Обновил данные — повтори изменение.');
 return warehousePanel(db,member,homepage,'Сохранено: **'+choice.spec.label+' · '+choice.expected+' → '+quantity+' шт.**');
}
