package com.luma.browser.ui

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.bumptech.glide.Glide
import com.luma.browser.R
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

data class HistoryEntry(
    val url: String,
    val title: String,
    val timestamp: Long = System.currentTimeMillis()
)

class HistoryAdapter(
    private val allItems: MutableList<HistoryEntry>,
    var isLight: Boolean = false,
    private val onItemClick: (String) -> Unit,
    private val onItemDelete: (HistoryEntry) -> Unit
) : RecyclerView.Adapter<HistoryAdapter.VH>() {

    private var filteredList = ArrayList(allItems)
    private val timeFormat = SimpleDateFormat("d MMM, HH:mm", Locale.getDefault())
    private var currentFilter = ""

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val favicon: ImageView = view.findViewById(R.id.itemFavicon)
        val title: TextView = view.findViewById(R.id.itemTitle)
        val subtitle: TextView = view.findViewById(R.id.itemSubtitle)
        val deleteBtn: ImageButton = view.findViewById(R.id.itemDeleteBtn)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_history_bookmark, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        if (position >= filteredList.size) return
        val item = filteredList[position]
        val url = item.url
        val title = item.title.ifBlank { extractDomain(url) }

        holder.title.text = title
        val timeStr = timeFormat.format(Date(item.timestamp))
        val domain = extractDomain(url)
        holder.subtitle.text = "$timeStr • $domain"

        val textColor = if (isLight) 0xFF000000.toInt() else 0xFFFFFFFF.toInt()
        val subColor = if (isLight) 0xFF6C6C70.toInt() else 0xFF716C82.toInt()
        val deleteColor = if (isLight) 0xFF8E8E93.toInt() else 0xFF716C82.toInt()

        holder.title.setTextColor(textColor)
        holder.subtitle.setTextColor(subColor)
        holder.deleteBtn.setColorFilter(deleteColor)

        try {
            if (url.startsWith("http")) {
                Glide.with(holder.itemView.context)
                    .load("https://www.google.com/s2/favicons?sz=32&domain_url=$url")
                    .placeholder(R.drawable.ic_globe)
                    .error(R.drawable.ic_globe)
                    .into(holder.favicon)
            } else {
                holder.favicon.setImageResource(R.drawable.ic_globe)
            }
        } catch (_: Exception) {
            holder.favicon.setImageResource(R.drawable.ic_globe)
        }

        holder.itemView.setOnClickListener { onItemClick(url) }
        holder.deleteBtn.setOnClickListener {
            val idx = holder.bindingAdapterPosition
            if (idx != RecyclerView.NO_POSITION && idx < filteredList.size) {
                val target = filteredList[idx]
                onItemDelete(target)
                allItems.removeAll { it.url == target.url }
                filteredList.removeAt(idx)
                notifyItemRemoved(idx)
            }
        }
    }

    override fun getItemCount() = filteredList.size

    fun filter(query: String) {
        currentFilter = query.trim()
        filteredList.clear()
        if (currentFilter.isBlank()) {
            filteredList.addAll(allItems)
        } else {
            val q = currentFilter.lowercase()
            for (item in allItems) {
                if (item.title.lowercase().contains(q) || item.url.lowercase().contains(q)) {
                    filteredList.add(item)
                }
            }
        }
        notifyDataSetChanged()
    }

    fun updateData(newItems: List<HistoryEntry>) {
        allItems.clear()
        allItems.addAll(newItems)
        filter(currentFilter)
    }

    private fun extractDomain(url: String): String {
        return try {
            val uri = android.net.Uri.parse(url)
            val host = uri.host.orEmpty()
            if (host.startsWith("www.")) host.substring(4) else if (host.isNotBlank()) host else url
        } catch (_: Exception) {
            url.removePrefix("https://").removePrefix("http://")
        }
    }
}
