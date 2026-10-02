package com.luma.browser.ui

import android.util.Base64
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
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
        val mediaFrame: FrameLayout = view.findViewById(R.id.supportMsgMediaFrame)
        val image: ImageView = view.findViewById(R.id.supportMsgImage)
        val playIcon: ImageView = view.findViewById(R.id.supportMsgPlayIcon)
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

        // Collect all available image and video sources
        val base64Images = msg.imageBase64List.filter { it.isNotBlank() }
        val urlImages = msg.imageUrls.filter { it.isNotBlank() && it != "null" && (it.startsWith("http://") || it.startsWith("https://")) }
        val videoThumbnails = msg.videoThumbnailBitmaps
        val urlVideos = msg.videoUrls.filter { it.isNotBlank() && it != "null" && (it.startsWith("http://") || it.startsWith("https://")) }

        val hasVideo = videoThumbnails.isNotEmpty() || urlVideos.isNotEmpty()
        val totalMedia = base64Images.size + urlImages.size + (if (hasVideo) 1 else 0)

        if (totalMedia == 0) {
            // No media at all
            holder.mediaFrame.visibility = View.GONE
            holder.playIcon.visibility = View.GONE
            holder.image.setOnClickListener(null)
            holder.multiScroll.visibility = View.GONE
            holder.multiContainer.removeAllViews()
        } else if (totalMedia == 1) {
            // Single media (image or video)
            holder.mediaFrame.visibility = View.VISIBLE
            holder.multiScroll.visibility = View.GONE
            holder.multiContainer.removeAllViews()

            if (hasVideo) {
                // Video item with play overlay
                holder.playIcon.visibility = View.VISIBLE
                if (videoThumbnails.isNotEmpty()) {
                    holder.image.setImageBitmap(videoThumbnails[0])
                } else if (urlVideos.isNotEmpty()) {
                    Glide.with(holder.itemView.context)
                        .asBitmap()
                        .load(urlVideos[0])
                        .frame(1_000_000)
                        .transform(RoundedCorners(16))
                        .into(holder.image)
                }

                holder.mediaFrame.setOnClickListener {
                    val vidUrl = urlVideos.firstOrNull()
                    if (!vidUrl.isNullOrBlank()) {
                        try {
                            val intent = android.content.Intent(android.content.Intent.ACTION_VIEW).apply {
                                setDataAndType(android.net.Uri.parse(vidUrl), "video/*")
                                addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                            }
                            holder.itemView.context.startActivity(intent)
                        } catch (_: Exception) {}
                    }
                }
            } else {
                // Single image
                holder.playIcon.visibility = View.GONE
                if (base64Images.isNotEmpty()) {
                    val b64 = base64Images[0]
                    try {
                        val bytes = Base64.decode(b64, Base64.DEFAULT)
                        Glide.with(holder.itemView.context)
                            .asBitmap()
                            .load(bytes)
                            .transform(RoundedCorners(16))
                            .into(holder.image)
                    } catch (_: Exception) {}
                    holder.mediaFrame.setOnClickListener { onImageClick?.invoke(b64, true) }
                } else {
                    val url = urlImages[0]
                    Glide.with(holder.itemView.context)
                        .load(url)
                        .transform(RoundedCorners(16))
                        .into(holder.image)
                    holder.mediaFrame.setOnClickListener { onImageClick?.invoke(url, false) }
                }
            }
        } else {
            // Multiple media — display in horizontal scroll
            holder.mediaFrame.visibility = View.GONE
            holder.playIcon.visibility = View.GONE
            holder.mediaFrame.setOnClickListener(null)
            holder.multiScroll.visibility = View.VISIBLE
            holder.multiContainer.removeAllViews()

            val density = holder.itemView.resources.displayMetrics.density
            val itemSizePx = (130 * density).toInt()
            val marginPx = (8 * density).toInt()

            // If there's a video in multi-media
            if (hasVideo) {
                val vFrame = FrameLayout(holder.itemView.context).apply {
                    layoutParams = LinearLayout.LayoutParams(itemSizePx, itemSizePx).apply {
                        marginEnd = marginPx
                    }
                }
                val iv = ImageView(holder.itemView.context).apply {
                    layoutParams = FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT)
                    scaleType = ImageView.ScaleType.CENTER_CROP
                    setBackgroundResource(R.drawable.bg_surface_raised)
                }
                if (videoThumbnails.isNotEmpty()) {
                    iv.setImageBitmap(videoThumbnails[0])
                } else if (urlVideos.isNotEmpty()) {
                    Glide.with(holder.itemView.context)
                        .asBitmap()
                        .load(urlVideos[0])
                        .frame(1_000_000)
                        .transform(RoundedCorners(12))
                        .into(iv)
                }
                val playBadge = ImageView(holder.itemView.context).apply {
                    val pSize = (36 * density).toInt()
                    layoutParams = FrameLayout.LayoutParams(pSize, pSize).apply {
                        gravity = android.view.Gravity.CENTER
                    }
                    setBackgroundResource(R.drawable.bg_glass_pill)
                    setImageResource(R.drawable.ic_play)
                    setPadding((8 * density).toInt(), (8 * density).toInt(), (8 * density).toInt(), (8 * density).toInt())
                }
                vFrame.addView(iv)
                vFrame.addView(playBadge)
                vFrame.setOnClickListener {
                    val vidUrl = urlVideos.firstOrNull()
                    if (!vidUrl.isNullOrBlank()) {
                        try {
                            val intent = android.content.Intent(android.content.Intent.ACTION_VIEW).apply {
                                setDataAndType(android.net.Uri.parse(vidUrl), "video/*")
                                addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                            }
                            holder.itemView.context.startActivity(intent)
                        } catch (_: Exception) {}
                    }
                }
                holder.multiContainer.addView(vFrame)
            }

            // Add base64 images
            base64Images.forEach { b64 ->
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

            // Add URL images
            urlImages.forEach { url ->
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
