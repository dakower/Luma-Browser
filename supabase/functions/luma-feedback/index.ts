import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const cors={"Access-Control-Allow-Origin":"*","Access-Control-Allow-Headers":"authorization, x-client-info, apikey, content-type","Access-Control-Allow-Methods":"POST, OPTIONS"};
const json=(body:unknown,status=200)=>new Response(JSON.stringify(body),{status,headers:{...cors,"Content-Type":"application/json","Cache-Control":"no-store"}});
const allowedMime=new Set(["image/png","image/jpeg","image/webp","text/plain","application/json","application/zip","application/pdf"]);
const clean=(value:unknown,max=6000)=>String(value??"").trim().slice(0,max);
const fileName=(value:unknown)=>clean(value,120).replace(/[^a-zA-Z0-9а-яА-ЯёЁ._ -]/g,"_")||"attachment.bin";
const escapeHtml=(v:string)=>v.replace(/[&<>]/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;"}[c]!));
const statusLabel=(v:string)=>({new:"Новый",checking:"Проверяется",need_info:"Нужна информация",fixing:"Исправляется",fixed:"Исправлено",cannot_reproduce:"Не воспроизводится",duplicate:"Дубликат"} as Record<string,string>)[v]??v;

async function telegram(method:string,body:Record<string,unknown>){
 const token=Deno.env.get("TELEGRAM_BOT_TOKEN")??"";if(!token)return null;
 const r=await fetch("https://api.telegram.org/bot"+token+"/"+method,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
 if(!r.ok){console.error("telegram",method,r.status,await r.text());return null}return await r.json();
}

Deno.serve(async request=>{
 if(request.method==="OPTIONS")return new Response("ok",{headers:cors});
 if(request.method!=="POST")return json({error:"method_not_allowed"},405);
 const url=Deno.env.get("SUPABASE_URL")??"",anon=Deno.env.get("SUPABASE_ANON_KEY")??"",service=Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")??"";
 if(!url||!anon||!service)return json({error:"configuration_incomplete"},503);
 const authorization=request.headers.get("Authorization")??"";
 const userClient=createClient(url,anon,{global:{headers:{Authorization:authorization}},auth:{persistSession:false,autoRefreshToken:false}});
 const admin=createClient(url,service,{auth:{persistSession:false,autoRefreshToken:false}});
 const {data:{user}}=await userClient.auth.getUser();if(!user)return json({error:"unauthorized"},401);
 const [{data:profile},{data:access}]=await Promise.all([admin.from("profiles").select("display_name,role").eq("id",user.id).maybeSingle(),admin.from("beta_access").select("status").eq("user_id",user.id).maybeSingle()]);
 if(profile?.role!=="admin"&&access?.status!=="approved")return json({error:"beta_access_required"},403);
 let input:any;try{input=await request.json()}catch{return json({error:"invalid_json"},400)}
 const action=clean(input?.action,32);
 try{
  if(action==="list"){
   const {data:reports,error}=await admin.from("feedback_reports").select("id,category,subject,status,diagnostics,context,created_at,updated_at").eq("user_id",user.id).order("updated_at",{ascending:false}).limit(100);if(error)throw error;
   const ids=(reports??[]).map((x:any)=>x.id);let messages:any[]=[];
   if(ids.length){const q=await admin.from("feedback_messages").select("id,report_id,sender_type,sender_name,body,attachment_path,attachment_name,attachment_mime,created_at").in("report_id",ids).order("created_at");if(q.error)throw q.error;messages=q.data??[]}
   for(const message of messages)if(message.attachment_path){const signed=await admin.storage.from("feedback-files").createSignedUrl(message.attachment_path,900);message.attachment_url=signed.data?.signedUrl??null;delete message.attachment_path}
   const {data:tasks}=await admin.from("tester_tasks").select("id,title,description,sort_order").eq("active",true).order("sort_order");
   const {data:allProgress}=await admin.from("tester_task_progress").select("task_id,user_id,result");
   const ownProgress=(allProgress??[]).filter((item:any)=>item.user_id===user.id);
   const taskStats=(tasks??[]).map((task:any)=>{const rows=(allProgress??[]).filter((item:any)=>item.task_id===task.id);return{...task,result:ownProgress.find((item:any)=>item.task_id===task.id)?.result??null,started_count:rows.length,works_count:rows.filter((item:any)=>item.result==="works").length,bug_count:rows.filter((item:any)=>item.result==="bug").length}});
   return json({reports:reports??[],messages,tasks:taskStats});
  }
  if(action==="task"){
   const taskId=clean(input?.taskId,40),result=["started","works","bug"].includes(input?.result)?input.result:"started";if(!/^[0-9a-f-]{36}$/i.test(taskId))return json({error:"invalid_task"},400);
   const now=new Date().toISOString();const {error}=await admin.from("tester_task_progress").upsert({user_id:user.id,task_id:taskId,result,completed:result==="works",completed_at:result==="works"?now:null,started_at:now,updated_at:now});if(error)throw error;return json({ok:true});
  }
  if(action==="verification"){
   const reportId=clean(input?.reportId,40),outcome=input?.outcome==="failed"?"failed":"fixed";const found=await admin.from("feedback_reports").select("id").eq("id",reportId).eq("user_id",user.id).maybeSingle();if(!found.data)return json({error:"report_not_found"},404);
   const body=outcome==="fixed"?"Исправлено у меня":"Ошибка осталась";await admin.from("feedback_messages").insert({report_id:reportId,sender_type:"tester",sender_name:profile?.display_name||"Тестер",body});await admin.from("feedback_reports").update({status:outcome==="fixed"?"fixed":"checking",updated_at:new Date().toISOString()}).eq("id",reportId);return json({ok:true});
  }
  if(action!=="create"&&action!=="message")return json({error:"invalid_action"},400);
  let reportId=clean(input?.reportId,40),report:any=null,diagnosticPath:string|null=null;
  if(action==="create"){
   const category=["bug","crash","search","idea","other"].includes(input?.category)?input.category:"bug";
   const subject=clean(input?.subject,160);if(subject.length<2)return json({error:"subject_required"},400);
   const inserted=await admin.from("feedback_reports").insert({user_id:user.id,category,subject,diagnostics:input?.diagnostics&&typeof input.diagnostics==="object"?input.diagnostics:{},context:input?.context&&typeof input.context==="object"?input.context:{}}).select("*").single();if(inserted.error)throw inserted.error;report=inserted.data;reportId=report.id;
   diagnosticPath=`${user.id}/${reportId}/diagnostics.json`;const diagnosticBytes=new TextEncoder().encode(JSON.stringify({reportId,userId:user.id,email:user.email,createdAt:report.created_at,category,subject,diagnostics:report.diagnostics,context:report.context},null,2));const diagnosticUpload=await admin.storage.from("feedback-files").upload(diagnosticPath,diagnosticBytes,{contentType:"application/json",upsert:true});if(diagnosticUpload.error){console.error("diagnostics upload",diagnosticUpload.error);diagnosticPath=null}
  }else{
   const found=await admin.from("feedback_reports").select("*").eq("id",reportId).eq("user_id",user.id).maybeSingle();if(found.error||!found.data)return json({error:"report_not_found"},404);report=found.data;
  }
  const body=clean(input?.body);const attachment=input?.attachment;let attachmentPath:string|null=null,attachmentName:string|null=null,attachmentMime:string|null=null;
  if(attachment?.data){
   attachmentMime=clean(attachment.mime,80).toLowerCase();attachmentName=fileName(attachment.name);if(!allowedMime.has(attachmentMime))return json({error:"attachment_type"},400);
   const raw=String(attachment.data).replace(/^data:[^;]+;base64,/,"");let bytes:Uint8Array;try{bytes=Uint8Array.from(atob(raw),c=>c.charCodeAt(0))}catch{return json({error:"attachment_invalid"},400)}
   if(bytes.length>8*1024*1024)return json({error:"attachment_too_large"},413);attachmentPath=`${user.id}/${reportId}/${crypto.randomUUID()}-${attachmentName}`;
   const uploaded=await admin.storage.from("feedback-files").upload(attachmentPath,bytes,{contentType:attachmentMime,upsert:false});if(uploaded.error)throw uploaded.error;
  }
  if(!body&& !attachmentPath)return json({error:"message_required"},400);
  const msg=await admin.from("feedback_messages").insert({report_id:reportId,sender_type:"tester",sender_name:profile?.display_name||user.email||"Тестер",body,attachment_path:attachmentPath,attachment_name:attachmentName,attachment_mime:attachmentMime}).select("*").single();if(msg.error)throw msg.error;
  await admin.from("feedback_reports").update({updated_at:new Date().toISOString(),status:["fixed","cannot_reproduce","duplicate"].includes(report.status)?"checking":report.status}).eq("id",reportId);
  const chatId=Deno.env.get("TELEGRAM_ADMIN_CHAT_ID")??"";
  if(chatId){
   if(action==="create"){
    const text=`🐞 <b>Новый отчёт #${reportId.slice(0,8).toUpperCase()}</b>\n<b>Тестер:</b> ${escapeHtml(profile?.display_name||"Luma tester")}\n<b>UID:</b> <code>${user.id}</code>\n<b>Версия:</b> ${escapeHtml(clean(input?.diagnostics?.version,30)||"—")}\n<b>Категория:</b> ${escapeHtml(report.category)}\n<b>Тема:</b> ${escapeHtml(report.subject)}\n\n${escapeHtml(body||"Вложение без текста")}`;
    const sent=await telegram("sendMessage",{chat_id:chatId,text,parse_mode:"HTML",reply_markup:{inline_keyboard:[[{text:"👀 Проверяется",callback_data:`status:${reportId}:checking`},{text:"❓ Нужна информация",callback_data:`status:${reportId}:need_info`}],[{text:"🛠 Исправляется",callback_data:`status:${reportId}:fixing`},{text:"✅ Исправлено",callback_data:`status:${reportId}:fixed`}],[{text:"Не воспроизводится",callback_data:`status:${reportId}:cannot_reproduce`},{text:"Дубликат",callback_data:`status:${reportId}:duplicate`}]]}});
    const messageId=sent?.result?.message_id;if(messageId)await admin.from("feedback_reports").update({telegram_message_id:messageId}).eq("id",reportId);
   }else if(report.telegram_message_id)await telegram("sendMessage",{chat_id:chatId,reply_to_message_id:report.telegram_message_id,text:`💬 <b>${escapeHtml(profile?.display_name||"Тестер")}</b> · #${reportId.slice(0,8).toUpperCase()}\n${escapeHtml(body||"Прикрепил файл")}`,parse_mode:"HTML"});
   if(diagnosticPath){const signed=await admin.storage.from("feedback-files").createSignedUrl(diagnosticPath,900);if(signed.data?.signedUrl)await telegram("sendDocument",{chat_id:chatId,document:signed.data.signedUrl,caption:`#${reportId.slice(0,8).toUpperCase()} · автоматическая диагностика`});}
   if(attachmentPath){const signed=await admin.storage.from("feedback-files").createSignedUrl(attachmentPath,900);const signedUrl=signed.data?.signedUrl;if(signedUrl)await telegram(attachmentMime?.startsWith("image/")?"sendPhoto":"sendDocument",{chat_id:chatId,[attachmentMime?.startsWith("image/")?"photo":"document"]:signedUrl,caption:`#${reportId.slice(0,8).toUpperCase()} · ${attachmentName}`});}
  }
  return json({ok:true,reportId,messageId:msg.data.id});
 }catch(error){console.error("luma-feedback",error);return json({error:"feedback_unavailable"},500)}
});
