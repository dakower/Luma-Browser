package com.luma.browser.ui

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.luma.browser.R
import com.luma.browser.ai.AiMessage
import io.noties.markwon.Markwon

class AiChatAdapter(private val messages: MutableList<AiMessage>) :
    RecyclerView.Adapter<AiChatAdapter.VH>() {

    private var markwon: Markwon? = null

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val role: TextView = view.findViewById(R.id.msgRole)
        val text: TextView = view.findViewById(R.id.msgText)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        if (markwon == null) {
            markwon = Markwon.create(parent.context)
        }
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_ai_message, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val msg = messages[position]
        val isUser = msg.role == "user"

        holder.role.text = if (isUser) "ВЫ" else "✦ LUMAAI"

        if (!isUser && msg.content.isBlank()) {
            holder.text.text = "LumaAI думает..."
            holder.text.setTextColor(0xFF716C82.toInt())
        } else {
            holder.text.setTextColor(0xFFFFFFFF.toInt())
            val m = markwon
            if (!isUser && m != null && msg.content.isNotBlank()) {
                m.setMarkdown(holder.text, msg.content)
            } else {
                holder.text.text = msg.content
            }
        }

        val params = holder.text.layoutParams as? ViewGroup.MarginLayoutParams
        if (isUser) {
            holder.role.textAlignment = View.TEXT_ALIGNMENT_TEXT_END
            params?.marginStart = 80
            params?.marginEnd = 0
            holder.text.setBackgroundResource(R.drawable.bg_surface_raised)
        } else {
            holder.role.textAlignment = View.TEXT_ALIGNMENT_TEXT_START
            params?.marginStart = 0
            params?.marginEnd = 40
            holder.text.setBackgroundResource(R.drawable.bg_glass_card)
        }
        holder.text.layoutParams = params
    }

    override fun onBindViewHolder(holder: VH, position: Int, payloads: MutableList<Any>) {
        if (payloads.isNotEmpty()) {
            val msg = messages[position]
            val isUser = msg.role == "user"
            if (!isUser && msg.content.isBlank()) {
                holder.text.text = "LumaAI думает..."
                holder.text.setTextColor(0xFF716C82.toInt())
            } else {
                holder.text.setTextColor(0xFFFFFFFF.toInt())
                val m = markwon
                if (!isUser && m != null && msg.content.isNotBlank()) {
                    m.setMarkdown(holder.text, msg.content)
                } else {
                    holder.text.text = msg.content
                }
            }
            return
        }
        super.onBindViewHolder(holder, position, payloads)
    }

    override fun getItemCount() = messages.size

    fun appendToLastAssistant(chunk: String) {
        val lastIdx = messages.indexOfLast { it.role == "assistant" }
        if (lastIdx >= 0) {
            val old = messages[lastIdx]
            messages[lastIdx] = old.copy(content = old.content + chunk)
            notifyItemChanged(lastIdx, PAYLOAD_UPDATE)
        }
    }

    fun addMessage(msg: AiMessage) {
        messages.add(msg)
        notifyItemInserted(messages.size - 1)
    }

    fun clear() {
        messages.clear()
        notifyDataSetChanged()
    }

    companion object {
        private const val PAYLOAD_UPDATE = "PAYLOAD_UPDATE"
    }
}
