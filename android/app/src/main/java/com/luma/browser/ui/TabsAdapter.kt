package com.luma.browser.ui

import android.graphics.Bitmap
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.bumptech.glide.Glide
import com.luma.browser.R
import com.luma.browser.tabs.LumaTab

class TabsAdapter(
    private val tabs: MutableList<LumaTab>,
    private val activeTabId: String?,
    private val onTabClick: (LumaTab) -> Unit,
    private val onTabClose: (LumaTab) -> Unit
) : RecyclerView.Adapter<TabsAdapter.VH>() {

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val favicon: ImageView = view.findViewById(R.id.tabFavicon)
        val title: TextView = view.findViewById(R.id.tabTitle)
        val url: TextView = view.findViewById(R.id.tabUrl)
        val close: ImageButton = view.findViewById(R.id.tabClose)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_tab, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val tab = tabs[position]

        // Show active tab with Luma's ActiveTabBrush gradient
        holder.itemView.isSelected = tab.id == activeTabId
        if (tab.id == activeTabId) {
            holder.itemView.setBackgroundResource(R.drawable.bg_tab_active)
        } else {
            holder.itemView.setBackgroundResource(R.drawable.bg_tab_inactive)
        }

        holder.title.text = tab.title.ifBlank { "Новая вкладка" }

        val displayUrl = when {
            tab.isHome -> "Главная страница"
            tab.url.startsWith("https://") -> tab.url.removePrefix("https://").let {
                if (it.length > 40) it.take(40) + "…" else it
            }
            else -> tab.url.take(40)
        }
        holder.url.text = displayUrl

        if (tab.favicon != null) {
            holder.favicon.setImageBitmap(tab.favicon)
        } else if (tab.url.isNotBlank() && !tab.isHome) {
            Glide.with(holder.favicon)
                .load("https://www.google.com/s2/favicons?sz=32&domain_url=${tab.url}")
                .placeholder(R.drawable.ic_globe)
                .into(holder.favicon)
        } else {
            holder.favicon.setImageResource(R.drawable.ic_home)
        }

        holder.itemView.setOnClickListener { onTabClick(tab) }
        holder.close.setOnClickListener { onTabClose(tab) }
    }

    override fun getItemCount() = tabs.size
}
