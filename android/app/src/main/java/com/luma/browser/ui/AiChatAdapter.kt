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
        holder.role.text = if (msg.role == "user") "Вы" else "✦ LumaAI"

        val m = markwon
        if (msg.role != "user" && m != null && msg.content.isNotBlank()) {
            m.setMarkdown(holder.text, msg.content)
        } else {
            holder.text.text = msg.content
        }

        // Align user messages right, assistant messages left
        val params = holder.text.layoutParams as? ViewGroup.MarginLayoutParams
        if (msg.role == "user") {
            holder.role.textAlignment = View.TEXT_ALIGNMENT_TEXT_END
            params?.marginStart = 60
            params?.marginEnd = 0
            holder.text.setBackgroundResource(R.drawable.bg_surface_raised)
        } else {
            holder.role.textAlignment = View.TEXT_ALIGNMENT_TEXT_START
            params?.marginStart = 0
            params?.marginEnd = 60
            holder.text.setBackgroundResource(R.drawable.bg_glass_card)
        }
        holder.text.layoutParams = params
    }

    override fun getItemCount() = messages.size

    fun appendToLastAssistant(chunk: String) {
        val lastIdx = messages.indexOfLast { it.role == "assistant" }
        if (lastIdx >= 0) {
            val updated = messages[lastIdx].copy(content = messages[lastIdx].content + chunk)
            messages[lastIdx] = updated
            notifyItemChanged(lastIdx)
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
}
