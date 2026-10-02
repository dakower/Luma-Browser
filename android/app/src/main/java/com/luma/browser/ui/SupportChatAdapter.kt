package com.luma.browser.ui

import android.util.Base64
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.HorizontalScrollView
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
    private val onImageClick: ((urlOrB64: String, isBase64: Boolean) -> Unit)? = null
) : RecyclerView.Adapter<SupportChatAdapter.VH>() {

    private val timeFormat = SimpleDateFormat("HH:mm", Locale.getDefault())

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val role: TextView = view.findViewById(R.id.supportMsgRole)
        val bubble: LinearLayout = view.findViewById(R.id.supportMsgBubble)
        val image: ImageView = view.findViewById(R.id.supportMsgImage)
        val multiScroll: HorizontalScrollView = view.findViewById(R.id.supportMsgMultiScroll)
        val multiContainer: LinearLayout = view.findViewById(R.id.supportMsgMultiContainer)
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

        // Collect all available image sources (base64 or remote URLs)
        val base64Items = msg.imageBase64List.filter { it.isNotBlank() }
        val urlItems = msg.imageUrls.filter { it.isNotBlank() && it != "null" && (it.startsWith("http://") || it.startsWith("https://")) }
        val totalImages = base64Items.size + urlItems.size

        if (totalImages == 0) {
            // No images at all
            holder.image.visibility = View.GONE
            holder.image.setOnClickListener(null)
            holder.multiScroll.visibility = View.GONE
            holder.multiContainer.removeAllViews()
        } else if (totalImages == 1) {
            // Single image
            holder.image.visibility = View.VISIBLE
            holder.multiScroll.visibility = View.GONE
            holder.multiContainer.removeAllViews()

            if (base64Items.isNotEmpty()) {
                val b64 = base64Items[0]
                try {
                    val bytes = Base64.decode(b64, Base64.DEFAULT)
                    Glide.with(holder.itemView.context)
                        .asBitmap()
                        .load(bytes)
                        .transform(RoundedCorners(16))
                        .into(holder.image)
                } catch (_: Exception) {}
                holder.image.setOnClickListener { onImageClick?.invoke(b64, true) }
            } else {
                val url = urlItems[0]
                Glide.with(holder.itemView.context)
                    .load(url)
                    .transform(RoundedCorners(16))
                    .into(holder.image)
                holder.image.setOnClickListener { onImageClick?.invoke(url, false) }
            }
        } else {
            // Multiple images — display in horizontal scroll
            holder.image.visibility = View.GONE
            holder.image.setOnClickListener(null)
            holder.multiScroll.visibility = View.VISIBLE
            holder.multiContainer.removeAllViews()

            val density = holder.itemView.resources.displayMetrics.density
            val itemSizePx = (130 * density).toInt()
            val marginPx = (8 * density).toInt()

            // Add base64 items
            base64Items.forEach { b64 ->
                val iv = ImageView(holder.itemView.context).apply {
                    layoutParams = LinearLayout.LayoutParams(itemSizePx, itemSizePx).apply {
                        marginEnd = marginPx
                    }
                    scaleType = ImageView.ScaleType.CENTER_CROP
                    setBackgroundResource(R.drawable.bg_surface_raised)
                }
                try {
                    val bytes = Base64.decode(b64, Base64.DEFAULT)
                    Glide.with(holder.itemView.context)
                        .asBitmap()
                        .load(bytes)
                        .transform(RoundedCorners(12))
                        .into(iv)
                } catch (_: Exception) {}
                iv.setOnClickListener { onImageClick?.invoke(b64, true) }
                holder.multiContainer.addView(iv)
            }

            // Add URL items
            urlItems.forEach { url ->
                val iv = ImageView(holder.itemView.context).apply {
                    layoutParams = LinearLayout.LayoutParams(itemSizePx, itemSizePx).apply {
                        marginEnd = marginPx
                    }
                    scaleType = ImageView.ScaleType.CENTER_CROP
                    setBackgroundResource(R.drawable.bg_surface_raised)
                }
                Glide.with(holder.itemView.context)
                    .load(url)
                    .transform(RoundedCorners(12))
                    .into(iv)
                iv.setOnClickListener { onImageClick?.invoke(url, false) }
                holder.multiContainer.addView(iv)
            }
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
