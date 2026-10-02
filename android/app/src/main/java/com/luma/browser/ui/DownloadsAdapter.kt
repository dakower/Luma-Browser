package com.luma.browser.ui

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.luma.browser.R
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

data class DownloadItem(
    val id: Long,
    val title: String,
    val uriString: String,
    val totalBytes: Long,
    val status: Int,
    val mediaType: String,
    val lastModified: Long
)

class DownloadsAdapter(
    private val allItems: MutableList<DownloadItem>,
    var isLight: Boolean = false,
    private val onItemClick: (DownloadItem) -> Unit,
    private val onShareClick: (DownloadItem) -> Unit,
    private val onDeleteClick: (DownloadItem) -> Unit
) : RecyclerView.Adapter<DownloadsAdapter.VH>() {

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val icon: ImageView = view.findViewById(R.id.itemDownloadIcon)
        val title: TextView = view.findViewById(R.id.itemDownloadTitle)
        val subtitle: TextView = view.findViewById(R.id.itemDownloadSubtitle)
        val shareBtn: ImageButton = view.findViewById(R.id.itemDownloadShare)
        val deleteBtn: ImageButton = view.findViewById(R.id.itemDownloadDelete)
        val card: View = view.findViewById(R.id.itemDownloadCard)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_download, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val item = allItems[position]
        holder.title.text = item.title

        val sizeText = formatFileSize(item.totalBytes)
        val dateText = formatDate(item.lastModified)
        holder.subtitle.text = "$sizeText • $dateText"

        val ext = item.title.substringAfterLast('.', "").lowercase()
        val iconRes = when (ext) {
            "apk" -> R.drawable.ic_brand_google
            "pdf", "doc", "docx", "txt" -> R.drawable.ic_book_open
            "jpg", "jpeg", "png", "webp", "gif" -> R.drawable.ic_spark
            "zip", "rar", "7z", "tar", "gz" -> R.drawable.ic_download
            else -> R.drawable.ic_download
        }
        holder.icon.setImageResource(iconRes)

        val textColor = if (isLight) 0xFF000000.toInt() else 0xFFFFFFFF.toInt()
        val subColor = if (isLight) 0xFF6C6C70.toInt() else 0xFF716C82.toInt()
        val cardBg = if (isLight) R.drawable.bg_safari_address_pill_light else R.drawable.bg_glass_card
        val iconColor = if (isLight) 0xFF000000.toInt() else 0xFFFFFFFF.toInt()

        holder.card.setBackgroundResource(cardBg)
        holder.title.setTextColor(textColor)
        holder.subtitle.setTextColor(subColor)
        holder.icon.setColorFilter(if (isLight) 0xFF7468C7.toInt() else 0xFFA9A2D8.toInt())
        holder.shareBtn.setColorFilter(iconColor)
        holder.deleteBtn.setColorFilter(iconColor)

        holder.itemView.setOnClickListener { onItemClick(item) }
        holder.shareBtn.setOnClickListener { onShareClick(item) }
        holder.deleteBtn.setOnClickListener {
            val idx = holder.bindingAdapterPosition
            if (idx != RecyclerView.NO_POSITION && idx < allItems.size) {
                onDeleteClick(item)
                allItems.removeAt(idx)
                notifyItemRemoved(idx)
            }
        }
    }

    override fun getItemCount() = allItems.size

    private fun formatFileSize(bytes: Long): String {
        if (bytes <= 0) return "Размер неизвестен"
        val kb = bytes / 1024.0
        val mb = kb / 1024.0
        val gb = mb / 1024.0
        return when {
            gb >= 1.0 -> String.format(Locale.US, "%.1f ГБ", gb)
            mb >= 1.0 -> String.format(Locale.US, "%.1f МБ", mb)
            kb >= 1.0 -> String.format(Locale.US, "%.1f КБ", kb)
            else -> "$bytes Б"
        }
    }

    private fun formatDate(timestamp: Long): String {
        if (timestamp <= 0) return "Недавно"
        return SimpleDateFormat("d MMM, HH:mm", Locale.getDefault()).format(Date(timestamp))
    }

    fun updateData(newItems: List<DownloadItem>) {
        allItems.clear()
        allItems.addAll(newItems)
        notifyDataSetChanged()
    }
}

