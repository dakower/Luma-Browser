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
import com.luma.browser.tabs.LumaTab

class TabsAdapter(
    private val allTabs: MutableList<LumaTab>,
    private val activeTabId: String?,
    private var currentSpaceId: String,
    private val onTabClick: (LumaTab) -> Unit,
    private val onTabClose: (LumaTab) -> Unit
) : RecyclerView.Adapter<TabsAdapter.VH>() {

    private var visibleTabs = mutableListOf<LumaTab>()

    init {
        updateVisibleTabs()
    }

    private fun updateVisibleTabs() {
        visibleTabs = allTabs.filter { it.spaceId == currentSpaceId }.toMutableList()
    }

    fun setSpaceId(spaceId: String) {
        currentSpaceId = spaceId
        updateVisibleTabs()
        notifyDataSetChanged()
    }

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
        val tab = visibleTabs[position]
        val isActive = tab.id == activeTabId

        if (isActive) {
            holder.itemView.setBackgroundResource(R.drawable.bg_tab_active)
        } else {
            holder.itemView.setBackgroundResource(R.drawable.bg_tab_inactive)
        }

        holder.title.text = if (tab.isHome) "Новая вкладка" else tab.title.ifBlank { "Страница" }
        holder.url.text = if (tab.isHome) "Luma Home" else tab.url.removePrefix("https://").removePrefix("http://")

        if (tab.favicon != null) {
            holder.favicon.setImageBitmap(tab.favicon)
        } else if (tab.isHome) {
            holder.favicon.setImageResource(R.drawable.luma_logo)
        } else if (tab.url.startsWith("http")) {
            Glide.with(holder.favicon)
                .load("https://www.google.com/s2/favicons?sz=32&domain_url=${tab.url}")
                .placeholder(R.drawable.ic_globe)
                .into(holder.favicon)
        } else {
            holder.favicon.setImageResource(R.drawable.ic_globe)
        }

        holder.itemView.setOnClickListener { onTabClick(tab) }
        holder.close.setOnClickListener {
            onTabClose(tab)
            updateVisibleTabs()
            notifyDataSetChanged()
        }
    }

    override fun getItemCount() = visibleTabs.size
}
