package com.luma.browser.ui

import android.util.Base64
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.bumptech.glide.Glide
import com.bumptech.glide.load.resource.bitmap.RoundedCorners
import com.luma.browser.R
import com.luma.browser.support.SupportChatMessage
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class SupportChatAdapter(
    private val messages: MutableList<SupportChatMessage>,
    private val onImageClick: ((SupportChatMessage) -> Unit)? = null
) : RecyclerView.Adapter<SupportChatAdapter.VH>() {

    private val timeFormat = SimpleDateFormat("HH:mm", Locale.getDefault())

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val role: TextView = view.findViewById(R.id.supportMsgRole)
        val bubble: LinearLayout = view.findViewById(R.id.supportMsgBubble)
        val image: ImageView = view.findViewById(R.id.supportMsgImage)
        val text: TextView = view.findViewById(R.id.supportMsgText)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_support_message, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val msg = messages[position]
        val timeStr = timeFormat.format(Date(msg.timestamp))
        holder.role.text = if (msg.isUser) "Вы • $timeStr" else "Создатель • $timeStr"

        if (msg.text.isNotBlank()) {
            holder.text.visibility = View.VISIBLE
            holder.text.text = msg.text
        } else {
            holder.text.visibility = View.GONE
        }

        // Image attachment
        val hasImage = !msg.imageBase64.isNullOrBlank() || !msg.imageUrl.isNullOrBlank()
        if (hasImage) {
            holder.image.visibility = View.VISIBLE
            try {
                if (!msg.imageBase64.isNullOrBlank()) {
                    val bytes = Base64.decode(msg.imageBase64, Base64.DEFAULT)
                    Glide.with(holder.itemView.context)
                        .asBitmap()
                        .load(bytes)
                        .transform(RoundedCorners(16))
                        .into(holder.image)
                } else if (!msg.imageUrl.isNullOrBlank()) {
                    Glide.with(holder.itemView.context)
                        .load(msg.imageUrl)
                        .transform(RoundedCorners(16))
                        .into(holder.image)
                }
            } catch (_: Exception) {}

            holder.image.setOnClickListener {
                onImageClick?.invoke(msg)
            }
        } else {
            holder.image.visibility = View.GONE
            holder.image.setOnClickListener(null)
        }

        val params = holder.bubble.layoutParams as? ViewGroup.MarginLayoutParams
        if (msg.isUser) {
            holder.role.textAlignment = View.TEXT_ALIGNMENT_TEXT_END
            params?.marginStart = 60
            params?.marginEnd = 0
            holder.bubble.setBackgroundResource(R.drawable.bg_surface_raised)
        } else {
            holder.role.textAlignment = View.TEXT_ALIGNMENT_TEXT_START
            params?.marginStart = 0
            params?.marginEnd = 60
            holder.bubble.setBackgroundResource(R.drawable.bg_glass_card)
        }
        holder.bubble.layoutParams = params
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
