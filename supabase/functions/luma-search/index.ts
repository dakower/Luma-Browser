import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const cors={"Access-Control-Allow-Origin":"*","Access-Control-Allow-Headers":"authorization, x-client-info, apikey, content-type","Access-Control-Allow-Methods":"POST, OPTIONS"};
const respond=(body:unknown,status=200)=>new Response(JSON.stringify(body),{status,headers:{...cors,"Content-Type":"application/json","Cache-Control":"no-store"}});
type Result={title:string;url:string;snippet:string;displayUrl?:string;image?:string;thumbnail?:string};
const plain=(value:string)=>value.replace(/<!\[CDATA\[([\s\S]*?)\]\]>/g,"$1").replace(/<[^>]+>/g," ").replace(/&quot;/g,'"').replace(/&#39;|&apos;/g,"'").replace(/&amp;/g,"&").replace(/&lt;/g,"<").replace(/&gt;/g,">").replace(/\s+/g," ").trim();
const safe=(value:string)=>{try{const u=new URL(value);return u.protocol==="http:"||u.protocol==="https:"?u.href:""}catch{return""}};
const fetchText=(url:string,timeout=9000)=>fetch(url,{headers:{"User-Agent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0 Safari/537.36","Accept-Language":"ru-RU,ru;q=0.9,en;q=0.7"},signal:AbortSignal.timeout(timeout)}).then(async r=>r.ok?await r.text():"").catch(()=>"");
const tag=(xml:string,name:string)=>plain(xml.match(new RegExp(`<${name}[^>]*>([\\s\\S]*?)<\\/${name}>`,"i"))?.[1]??"");
const blockedHost=(url:string)=>{try{const h=new URL(url).hostname.replace(/^www\./,"");return ["bing.com","google.com","duckduckgo.com"].some(x=>h===x||h.endsWith("."+x))}catch{return true}};
const abbreviations:Record<string,string>={"вк":"вконтакте vk","vk":"vkontakte вконтакте","тг":"telegram телеграм","tg":"telegram телеграм","ют":"youtube ютуб","ютуб":"youtube","yt":"youtube","инста":"instagram","инст":"instagram","insta":"instagram","ig":"instagram","дс":"discord","dc":"discord","гх":"github","gh":"github","споти":"spotify","ям":"яндекс музыка","sc":"soundcloud","реддит":"reddit","тт":"tiktok","гпт":"chatgpt","ии":"искусственный интеллект ai","кс":"counter strike","кс2":"counter strike 2","майн":"minecraft","гта":"gta","пк":"компьютер pc","ноут":"ноутбук laptop"};
const cyrToLatin:Record<string,string>={а:"a",б:"b",в:"v",г:"g",д:"d",е:"e",ё:"e",ж:"zh",з:"z",и:"i",й:"y",к:"k",л:"l",м:"m",н:"n",о:"o",п:"p",р:"r",с:"s",т:"t",у:"u",ф:"f",х:"h",ц:"ts",ч:"ch",ш:"sh",щ:"sch",ы:"y",э:"e",ю:"yu",я:"ya",і:"i",ї:"yi",є:"ye"};
const latinToCyrillic=(value:string)=>value.toLowerCase().replace(/shch|sch|yo|zh|kh|ts|ch|sh|yu|ya|ye|[a-z]/g,part=>({shch:"щ",sch:"щ",yo:"ё",zh:"ж",kh:"х",ts:"ц",ch:"ч",sh:"ш",yu:"ю",ya:"я",ye:"е",a:"а",b:"б",c:"к",d:"д",e:"е",f:"ф",g:"г",h:"х",i:"и",j:"дж",k:"к",l:"л",m:"м",n:"н",o:"о",p:"п",q:"к",r:"р",s:"с",t:"т",u:"у",v:"в",w:"в",x:"кс",y:"й",z:"з"} as Record<string,string>)[part]??part);
const cyrillicToLatin=(value:string)=>value.toLowerCase().replace(/[а-яёіїє]/g,part=>cyrToLatin[part]??part);
const swapLayout=(value:string)=>{const latin="qwertyuiop[]asdfghjkl;'zxcvbnm,.`",cyrillic="йцукенгшщзхъфывапролджэячсмитьбюё",toCyr=/[a-z]/i.test(value);return [...value].map(char=>{const lower=char.toLowerCase(),index=(toCyr?latin:cyrillic).indexOf(lower);return index<0?char:(toCyr?cyrillic:latin)[index]}).join("")};
const dorkSource="site|filetype|ext|intitle|allintitle|inurl|allinurl|intext|allintext|before|after|cache|related|link|define|source|weather|stocks|map|movie";
const dorkRegex=new RegExp(`(^|[^\\p{L}\\p{N}_])(-?)(${dorkSource}):("[^"]+"|[^\\s]+)`,`giu`);
const protectedSyntaxRegex=new RegExp(`(^|[^\\p{L}\\p{N}_])-?(?:${dorkSource}):(?:"[^"]+"|[^\\s]+)|"[^"]+"|\\b(?:OR|AND)\\b`,`giu`);
const hasGoogleDork=(query:string)=>{dorkRegex.lastIndex=0;return dorkRegex.test(query)||/(?:^|\s)(?:OR|AND)(?:\s|$)|"[^"]+"/i.test(query)};
const transformFreeText=(query:string,transform:(value:string)=>string)=>{const parts:string[]=[];protectedSyntaxRegex.lastIndex=0;const masked=query.replace(protectedSyntaxRegex,match=>{parts.push(match);return `\uE000${parts.length-1}\uE001`});let output=transform(masked);parts.forEach((part,index)=>output=output.replace(`\uE000${index}\uE001`,part));return output};
const queryVariants=(query:string,limit=4)=>{const out:string[]=[];const add=(value:string)=>{value=value.trim().replace(/\s+/g," ");if(value.length>1&&!out.some(item=>item.toLowerCase()===value.toLowerCase()))out.push(value)};add(query);add(transformFreeText(query,value=>value.replace(/[\p{L}\p{N}]+/gu,token=>abbreviations[token.toLowerCase()]??token)));if(/[a-z]/i.test(query))add(transformFreeText(query,latinToCyrillic));if(/[а-яёіїє]/i.test(query))add(transformFreeText(query,cyrillicToLatin));add(transformFreeText(query,swapLayout));return out.slice(0,limit)};
const matchesGoogleDorks=(query:string,item:Result)=>{dorkRegex.lastIndex=0;for(const match of query.matchAll(dorkRegex)){const exclude=match[2]==="-",operator=match[3].toLowerCase(),value=match[4].replace(/^"|"$/g,"").replace(/^[*.]+/,"").toLowerCase(),address=item.url.toLowerCase(),title=plain(item.title).toLowerCase(),snippet=plain(item.snippet).toLowerCase();let matches=true;try{const host=new URL(item.url).hostname.toLowerCase();if(operator==="site")matches=host===value||host.endsWith("."+value);else if(operator==="filetype"||operator==="ext")matches=new RegExp(`\\.${value.replace(/[^a-z0-9]/gi,"")}(?:$|[?#])`,"i").test(address);else if(operator==="intitle"||operator==="allintitle")matches=title.includes(value);else if(operator==="inurl"||operator==="allinurl")matches=address.includes(value);else if(operator==="intext"||operator==="allintext")matches=snippet.includes(value)}catch{matches=false}if(exclude?matches:!matches)return false}return true};
const ranked=(query:string,items:Result[])=>{const qs=queryVariants(query,5),terms=[...new Set(qs.flatMap(q=>q.toLowerCase().match(/[\p{L}\p{N}]{2,}/gu)??[]))];return items.map((item,index)=>{const title=plain(item.title).toLowerCase(),snippet=plain(item.snippet).toLowerCase(),address=(item.displayUrl??"")+" "+item.url.toLowerCase();let score=terms.reduce((sum,term)=>sum+(title.includes(term)?6:0)+(address.includes(term)?3:0)+(snippet.includes(term)?1:0),0);for(const variant of qs){const phrase=plain(variant).toLowerCase(),compact=phrase.replace(/[^\p{L}\p{N}]/gu,"");if(phrase.length>2&&title.includes(phrase))score+=10;if(compact.length>2&&address.replace(/[^\p{L}\p{N}]/gu,"").includes(compact))score+=12}if(/wikipedia\.org|youtu(?:be\.com|\.be)/i.test(address))score-=2;return{item,index,score}}).sort((a,b)=>b.score-a.score||a.index-b.index).map(x=>x.item)};

async function rss(query:string,news=false):Promise<Result[]>{
 const endpoint=news?"https://www.bing.com/news/search?format=rss&mkt=ru-RU&q=":"https://www.bing.com/search?format=rss&mkt=ru-RU&q=";
 const xml=await fetchText(endpoint+encodeURIComponent(query));
 return [...xml.matchAll(/<item>([\s\S]*?)<\/item>/gi)].slice(0,14).map(m=>({title:tag(m[1],"title"),url:safe(tag(m[1],"link")),snippet:tag(m[1],"description")})).filter(x=>x.title&&x.url&&!blockedHost(x.url));
}
async function lite(query:string):Promise<Result[]>{
 const html=await fetchText("https://lite.duckduckgo.com/lite/?q="+encodeURIComponent(query));const out:Result[]=[];
 for(const m of html.matchAll(/<a[^>]+href=["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/gi)){
  let url=m[1].replace(/&amp;/g,"&");try{const redirect=new URL(url,"https://lite.duckduckgo.com").searchParams.get("uddg");if(redirect)url=redirect}catch{}
  url=safe(url);const title=plain(m[2]);if(!url||!title||blockedHost(url))continue;
  const after=html.slice((m.index??0)+m[0].length,(m.index??0)+m[0].length+1000);const snippet=plain(after.match(/<(?:td|div)[^>]*class=["'][^"']*(?:result-snippet|snippet)[^"']*["'][^>]*>([\s\S]*?)<\/(?:td|div)>/i)?.[1]??"");
  out.push({title,url,snippet});if(out.length>=14)break;
 }return out;
}
async function webHtml(query:string):Promise<Result[]>{
 const html=await fetchText("https://www.bing.com/search?setlang=ru&cc=ru&count=30&q="+encodeURIComponent(query),11000),out:Result[]=[];
 for(const block of html.matchAll(/<li[^>]*class=["'][^"']*\bb_algo\b[^"']*["'][^>]*>([\s\S]*?)<\/li>/gi)){
  const link=block[1].match(/<h2[^>]*>[\s\S]*?<a[^>]+href=["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/i);if(!link)continue;
  const url=safe(link[1]),title=plain(link[2]),snippet=plain(block[1].match(/<p[^>]*>([\s\S]*?)<\/p>/i)?.[1]??"");
  if(!url||!title||blockedHost(url))continue;out.push({title,url,snippet});if(out.length>=24)break;
 }return out;
}
async function youtube(query:string):Promise<Result[]>{
 const html=await fetchText("https://www.youtube.com/results?hl=ru&search_query="+encodeURIComponent(query),11000),out:Result[]=[],seen=new Set<string>();
 const text=(value:string)=>{try{return JSON.parse('"'+value+'"')}catch{return plain(value)}};
 for(const m of html.matchAll(/"videoRenderer"\s*:\s*\{[\s\S]{0,700}?"videoId"\s*:\s*"([A-Za-z0-9_-]{6,})"[\s\S]{0,2200}?"title"\s*:\s*\{[\s\S]{0,500}?"text"\s*:\s*"((?:\\.|[^"\\])+)"/gi)){
  const id=m[1];if(seen.has(id))continue;seen.add(id);out.push({title:text(m[2]),url:"https:"+"//www.youtube.com/watch?v="+id,snippet:`Видео по запросу «${query}»`,thumbnail:"https:"+"//i.ytimg.com/vi/"+id+"/hqdefault.jpg"});if(out.length>=18)break;
 }return out;
}
async function jina(query:string):Promise<Result[]>{
 const key=Deno.env.get("JINA_API_KEY")??"";if(!key)return[];
 try{const r=await fetch("https://s.jina.ai/"+encodeURIComponent(query),{headers:{Authorization:`Bearer ${key}`,Accept:"text/plain","X-Respond-With":"markdown","X-Timeout":"16"},signal:AbortSignal.timeout(19000)});if(!r.ok)return[];const text=(await r.text()).slice(0,14000),out:Result[]=[];
  for(const m of text.matchAll(/\[([^\]\n]{2,220})\]\((https?:\/\/[^)\s]+)\)/g)){const url=safe(m[2]);if(!url||blockedHost(url))continue;const around=text.slice(Math.max(0,(m.index??0)-220),Math.min(text.length,(m.index??0)+m[0].length+420));out.push({title:plain(m[1]),url,snippet:plain(around.replace(m[0],""))});if(out.length>=10)break}return out;
 }catch{return[]}
}
async function googleDork(query:string):Promise<Result[]>{
 const markdown=await fetchText("https://r.jina.ai/http://www.google.com/search?num=30&filter=0&hl=ru&q="+encodeURIComponent(query),14000),out:Result[]=[];
 for(const m of markdown.matchAll(/\[([^\]\r\n]{2,220})\]\((https?:\/\/[^)\s]+)\)/g)){const url=safe(m[2]);if(!url||blockedHost(url))continue;const at=m.index??0,around=markdown.slice(Math.max(0,at-180),Math.min(markdown.length,at+m[0].length+520));out.push({title:plain(m[1]),url,snippet:plain(around.replace(m[0],""))});if(out.length>=24)break}return out;
}
async function images(query:string):Promise<Result[]>{
 const html=await fetchText("https://www.bing.com/images/search?form=HDRSC2&q="+encodeURIComponent(query),11000),out:Result[]=[];
 for(const m of html.matchAll(/<a[^>]+class=["'][^"']*iusc[^"']*["'][^>]+m=["']([^"']+)["'][^>]*>/gi)){
  try{const meta=JSON.parse(m[1].replace(/&quot;/g,'"').replace(/&amp;/g,"&"));const url=safe(meta.purl||meta.surl||""),image=safe(meta.murl||""),thumbnail=safe(meta.turl||"");if(!url||(!image&&!thumbnail))continue;out.push({title:plain(meta.t||meta.desc||query),url,snippet:"",image,thumbnail});if(out.length>=18)break}catch{}
 }return out;
}
const enrich=(query:string,mode:string)=>mode==="shopping"?`${query} купить цена магазин`:mode==="video"?`${query} видео`:mode==="shorts"?`${query} shorts короткое видео`:query;
async function search(query:string,mode:string):Promise<Result[]>{
 const dork=hasGoogleDork(query),q=dork?query:enrich(query,mode),variants=queryVariants(q,4);
 if(mode==="images"){
  const batches=await Promise.allSettled(variants.slice(0,3).map(value=>images(value))),seen=new Set<string>(),all:Result[]=[];
  for(const batch of batches)if(batch.status==="fulfilled")for(const item of batch.value){if(seen.has(item.url)||(dork&&!matchesGoogleDorks(query,item)))continue;seen.add(item.url);all.push(item)}return ranked(query,all).slice(0,18);
 }
 const work:Promise<Result[]>[]=[webHtml(variants[0]),rss(variants[0],mode==="news"),lite(variants[0]),jina(variants[0]),youtube(variants[0])];if(dork)work.unshift(googleDork(query));
 for(const alternative of variants.slice(1,3)){work.push(webHtml(alternative),lite(alternative));if(mode==="video"||mode==="shorts")work.push(youtube(alternative))}
 const batches=await Promise.allSettled(work),seen=new Set<string>(),all:Result[]=[];
 for(const batch of batches)if(batch.status==="fulfilled")for(const item of batch.value){const key=item.url.split("#")[0];if(!key||seen.has(key)||(dork&&!matchesGoogleDorks(query,item)))continue;if(mode==="shorts"&&!/(\/shorts\/|tiktok\.com|\/reel\/)/i.test(item.url+" "+item.title))continue;if(mode==="video"&&!/(youtube\.com|youtu\.be|vimeo\.com|rutube\.ru|vk\.com\/video|video)/i.test(item.url+" "+item.title))continue;seen.add(key);all.push({...item,displayUrl:new URL(item.url).hostname.replace(/^www\./,"")})}
 const hostCounts=new Map<string,number>(),output:Result[]=[];for(const item of ranked(query,all)){const host=item.displayUrl??new URL(item.url).hostname.replace(/^www\./,""),special=/wikipedia\.org|youtube\.com|youtu\.be/i.test(host),hostLimit=mode==="video"||mode==="shorts"?4:special?1:2,count=hostCounts.get(host)??0;if(count>=hostLimit)continue;hostCounts.set(host,count+1);output.push(item);if(output.length>=20)break}return output;
}
async function aiAnswer(query:string,results:Result[],authorization:string){
 const supabaseUrl=Deno.env.get("SUPABASE_URL")??"",anon=Deno.env.get("SUPABASE_ANON_KEY")??"",service=Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")??"",providerKey=Deno.env.get("LUMA_AI_API_KEY")??"",endpoint=Deno.env.get("LUMA_AI_ENDPOINT")??"https://api.deepseek.com/chat/completions";
 if(!supabaseUrl||!anon||!service||!providerKey)return{error:"Режим ИИ временно недоступен."};
 const userClient=createClient(supabaseUrl,anon,{global:{headers:{Authorization:authorization}},auth:{persistSession:false,autoRefreshToken:false}});const {data:{user}}=await userClient.auth.getUser();if(!user)return{error:"Войдите в аккаунт Luma, чтобы использовать режим ИИ."};
 const {data:quota,error}=await userClient.rpc("consume_my_assistant_quota");if(error)return{error:"Не удалось проверить лимит LumaAI."};if(!quota?.allowed)return{error:"Дневной лимит LumaAI исчерпан."};
 const context=results.slice(0,10).map((x,i)=>`[${i+1}] ${x.title}\n${x.url}\n${x.snippet}`).join("\n\n");
 try{const r=await fetch(endpoint,{method:"POST",headers:{Authorization:`Bearer ${providerKey}`,"Content-Type":"application/json"},body:JSON.stringify({model:Deno.env.get("LUMA_AI_FAST_MODEL")??"deepseek-flash",stream:false,temperature:.25,max_tokens:900,messages:[{role:"system",content:"Ты — поисковый режим LumaAI. Дай ясный ответ на русском только по найденным источникам. Не называй поставщиков поиска или моделей. Ставь ссылки на источники в формате [1], [2]. Если данных недостаточно, скажи об этом."},{role:"user",content:`Запрос: ${query}\n\nИсточники:\n${context}`}]})});if(!r.ok)throw new Error();const data=await r.json();return{answer:String(data?.choices?.[0]?.message?.content??"").trim()||"Не удалось сформировать ответ."};}
 catch{const admin=createClient(supabaseUrl,service,{auth:{persistSession:false,autoRefreshToken:false}});if(!quota.unlimited)await admin.rpc("refund_assistant_quota",{p_user_id:user.id,p_usage_date:quota.usage_date});return{error:"Не удалось сформировать ответ LumaAI."}}
}

Deno.serve(async request=>{
 if(request.method==="OPTIONS")return new Response("ok",{headers:cors});if(request.method!=="POST")return respond({error:"method_not_allowed"},405);
 let input:any;try{input=await request.json()}catch{return respond({error:"Некорректный запрос."},400)}
 const query=plain(String(input?.query??"")).slice(0,300),mode=["ai","all","images","shopping","video","shorts","news"].includes(input?.mode)?input.mode:"all";if(!query)return respond({error:"Введите поисковый запрос."},400);
 const started=Date.now(),results=await search(query,mode==="ai"?"all":mode);if(mode!=="ai")return respond({results,elapsedMs:Date.now()-started});
 const ai=await aiAnswer(query,results,request.headers.get("Authorization")??"");return respond({...ai,results,sources:results.slice(0,6),elapsedMs:Date.now()-started},ai.error?400:200);
});
