package com.luma.browser.ui

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.luma.browser.R
import com.luma.browser.support.SupportChatMessage
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class SupportChatAdapter(private val messages: MutableList<SupportChatMessage>) :
    RecyclerView.Adapter<SupportChatAdapter.VH>() {

    private val timeFormat = SimpleDateFormat("HH:mm", Locale.getDefault())

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val role: TextView = view.findViewById(R.id.msgRole)
        val text: TextView = view.findViewById(R.id.msgText)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_ai_message, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val msg = messages[position]
        val timeStr = timeFormat.format(Date(msg.timestamp))
        holder.role.text = if (msg.isUser) "Вы • $timeStr" else "Создатель • $timeStr"
        holder.text.text = msg.text

        val params = holder.text.layoutParams as? ViewGroup.MarginLayoutParams
        if (msg.isUser) {
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

    fun addMessage(msg: SupportChatMessage) {
        messages.add(msg)
        notifyItemInserted(messages.size - 1)
    }

    fun setMessages(newMessages: List<SupportChatMessage>) {
        messages.clear()
        messages.addAll(newMessages)
        notifyDataSetChanged()
    }
}
