import { createClient } from "https://esm.sh/@supabase/supabase-js@2";
const answer=(body:unknown={ok:true},status=200)=>new Response(JSON.stringify(body),{status,headers:{"Content-Type":"application/json","Cache-Control":"no-store"}});
const clean=(v:unknown,max=6000)=>String(v??"").trim().slice(0,max);
const labels:Record<string,string>={checking:"Проверяется",need_info:"Нужна информация",fixing:"Исправляется",fixed:"Исправлено",cannot_reproduce:"Не воспроизводится",duplicate:"Дубликат"};
async function telegram(token:string,method:string,body:Record<string,unknown>){await fetch("https://api.telegram.org/bot"+token+"/"+method,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)}).catch(()=>null)}
Deno.serve(async request=>{
 if(request.method!=="POST")return answer({error:"method_not_allowed"},405);
 const secret=Deno.env.get("TELEGRAM_WEBHOOK_SECRET")??"";if(!secret||request.headers.get("X-Telegram-Bot-Api-Secret-Token")!==secret)return answer({error:"forbidden"},403);
 const url=Deno.env.get("SUPABASE_URL")??"",key=Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")??"",token=Deno.env.get("TELEGRAM_BOT_TOKEN")??"",adminId=Deno.env.get("TELEGRAM_ADMIN_USER_ID")??"";if(!url||!key||!token||!adminId)return answer({error:"configuration_incomplete"},503);
 let update:any;try{update=await request.json()}catch{return answer({error:"invalid_json"},400)}
 const sender=String(update?.message?.from?.id??update?.callback_query?.from?.id??"");if(sender!==adminId)return answer();
 const admin=createClient(url,key,{auth:{persistSession:false,autoRefreshToken:false}});
 try{
  const callback=update?.callback_query;if(callback){const match=clean(callback.data,100).match(/^status:([0-9a-f-]{36}):(checking|need_info|fixing|fixed|cannot_reproduce|duplicate)$/i);if(!match)return answer();const reportId=match[1],status=match[2];await admin.from("feedback_reports").update({status,updated_at:new Date().toISOString()}).eq("id",reportId);await admin.from("feedback_messages").insert({report_id:reportId,sender_type:"system",sender_name:"dakower",body:`Статус изменён: ${labels[status]}`});await telegram(token,"answerCallbackQuery",{callback_query_id:callback.id,text:labels[status]});await telegram(token,"sendMessage",{chat_id:callback.message.chat.id,reply_to_message_id:callback.message.message_id,text:`Статус #${reportId.slice(0,8).toUpperCase()}: ${labels[status]}`});return answer()}
  const message=update?.message,text=clean(message?.text??message?.caption);const replyId=message?.reply_to_message?.message_id;if(!replyId||!text)return answer();const found=await admin.from("feedback_reports").select("id").eq("telegram_message_id",replyId).maybeSingle();if(!found.data)return answer();await admin.from("feedback_messages").insert({report_id:found.data.id,sender_type:"admin",sender_name:"dakower",body:text});await admin.from("feedback_reports").update({status:"checking",updated_at:new Date().toISOString()}).eq("id",found.data.id);await telegram(token,"sendMessage",{chat_id:message.chat.id,reply_to_message_id:replyId,text:`Ответ отправлен тестеру · #${found.data.id.slice(0,8).toUpperCase()}`});return answer();
 }catch(error){console.error("luma-feedback-telegram",error);return answer({error:"webhook_failed"},500)}
});
