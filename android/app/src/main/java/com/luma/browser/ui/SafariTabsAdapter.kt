package com.luma.browser.ui

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.bumptech.glide.Glide
import com.luma.browser.R
import com.luma.browser.tabs.LumaTab

class SafariTabsAdapter(
    private val allTabs: MutableList<LumaTab>,
    var activeTabId: String?,
    private val onTabClick: (LumaTab) -> Unit,
    private val onTabClose: (LumaTab) -> Unit
) : RecyclerView.Adapter<SafariTabsAdapter.VH>() {

    private var filteredTabs = mutableListOf<LumaTab>().apply { addAll(allTabs) }
    private var currentQuery = ""

    fun filter(query: String) {
        currentQuery = query.trim()
        filteredTabs.clear()
        if (currentQuery.isEmpty()) {
            filteredTabs.addAll(allTabs)
        } else {
            filteredTabs.addAll(allTabs.filter {
                it.title.contains(currentQuery, ignoreCase = true) ||
                it.url.contains(currentQuery, ignoreCase = true)
            })
        }
        notifyDataSetChanged()
    }

    fun syncTabs() {
        filter(currentQuery)
    }

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val thumbnail: ImageView = view.findViewById(R.id.tabThumbnail)
        val placeholder: View = view.findViewById(R.id.tabPlaceholder)
        val placeholderText: TextView = view.findViewById(R.id.tabPlaceholderText)
        val domainText: TextView = view.findViewById(R.id.tabCardDomain)
        val closeBtn: FrameLayout = view.findViewById(R.id.tabClose)
        val activeRing: View = view.findViewById(R.id.tabActiveRing)
        val favicon: ImageView = view.findViewById(R.id.tabFavicon)
        val title: TextView = view.findViewById(R.id.tabTitle)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_tab_safari, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val tab = filteredTabs[position]
        val isActive = tab.id == activeTabId

        // Active selection highlight ring
        holder.activeRing.visibility = if (isActive) View.VISIBLE else View.GONE

        // Domain in top mini address capsule
        val domain = when {
            tab.isHistory -> "История"
            tab.isDownloads -> "Загрузки"
            tab.isHome -> "Стартовая страница"
            else -> sanitizeDomain(tab.url, false)
        }
        holder.domainText.text = domain

        // Title in footer
        holder.title.text = when {
            tab.isHistory -> "История"
            tab.isDownloads -> "Загрузки"
            tab.isHome -> "Стартовая страница"
            else -> tab.title.ifBlank { domain }
        }

        // Preview thumbnail
        if (tab.thumbnail != null) {
            holder.thumbnail.visibility = View.VISIBLE
            holder.thumbnail.setImageBitmap(tab.thumbnail)
            holder.placeholder.visibility = View.GONE
        } else if (tab.isHome) {
            holder.thumbnail.visibility = View.GONE
            holder.placeholder.visibility = View.VISIBLE
            holder.placeholderText.text = "Luma"
        } else if (tab.isHistory) {
            holder.thumbnail.visibility = View.GONE
            holder.placeholder.visibility = View.VISIBLE
            holder.placeholderText.text = "История"
        } else if (tab.isDownloads) {
            holder.thumbnail.visibility = View.GONE
            holder.placeholder.visibility = View.VISIBLE
            holder.placeholderText.text = "Загрузки"
        } else {
            holder.thumbnail.visibility = View.GONE
            holder.placeholder.visibility = View.VISIBLE
            holder.placeholderText.text = domain
        }

        // Favicon
        if (tab.favicon != null) {
            holder.favicon.setImageBitmap(tab.favicon)
        } else if (tab.isHome) {
            holder.favicon.setImageResource(R.drawable.luma_logo)
        } else if (tab.isHistory) {
            holder.favicon.setImageResource(R.drawable.ic_history)
        } else if (tab.isDownloads) {
            holder.favicon.setImageResource(R.drawable.ic_download)
        } else if (tab.url.startsWith("http")) {
            try {
                Glide.with(holder.favicon.context)
                    .load("https://www.google.com/s2/favicons?sz=32&domain_url=${tab.url}")
                    .placeholder(R.drawable.ic_globe)
                    .error(R.drawable.ic_globe)
                    .into(holder.favicon)
            } catch (_: Exception) {
                holder.favicon.setImageResource(R.drawable.ic_globe)
            }
        } else {
            holder.favicon.setImageResource(R.drawable.ic_globe)
        }

        holder.itemView.setOnClickListener { onTabClick(tab) }
        holder.closeBtn.setOnClickListener {
            onTabClose(tab)
            syncTabs()
        }
    }

    private fun sanitizeDomain(url: String, isHome: Boolean): String {
        if (isHome || url.isBlank() || url.startsWith("luma://")) return "Стартовая страница"
        return try {
            val uri = android.net.Uri.parse(url)
            val host = uri.host.orEmpty()
            if (host.startsWith("www.")) host.substring(4) else if (host.isNotBlank()) host else url
        } catch (_: Exception) {
            url.removePrefix("https://").removePrefix("http://")
        }
    }

    override fun getItemCount() = filteredTabs.size
}
