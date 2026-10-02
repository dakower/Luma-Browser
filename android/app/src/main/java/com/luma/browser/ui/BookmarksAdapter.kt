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
import org.json.JSONObject

class BookmarksAdapter(
    private val allItems: MutableList<JSONObject>,
    private val onItemClick: (String) -> Unit,
    private val onItemDelete: (JSONObject) -> Unit
) : RecyclerView.Adapter<BookmarksAdapter.VH>() {

    private var filteredList = ArrayList(allItems)

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
        val item = filteredList[position]
        val url = item.optString("url", "")
        val title = item.optString("title", url)

        holder.title.text = if (title.isBlank()) url else title
        holder.subtitle.text = url

        if (url.startsWith("http")) {
            Glide.with(holder.favicon)
                .load("https://www.google.com/s2/favicons?sz=32&domain_url=$url")
                .placeholder(R.drawable.ic_bookmark)
                .into(holder.favicon)
        } else {
            holder.favicon.setImageResource(R.drawable.ic_bookmark)
        }

        holder.itemView.setOnClickListener { onItemClick(url) }
        holder.deleteBtn.setOnClickListener {
            onItemDelete(item)
            allItems.remove(item)
            filter(currentFilter)
        }
    }

    override fun getItemCount() = filteredList.size

    fun filter(query: String) {
        currentFilter = query
        filteredList.clear()
        if (query.isBlank()) {
            filteredList.addAll(allItems)
        } else {
            val q = query.lowercase()
            for (item in allItems) {
                val title = item.optString("title", "").lowercase()
                val url = item.optString("url", "").lowercase()
                if (title.contains(q) || url.contains(q)) {
                    filteredList.add(item)
                }
            }
        }
        notifyDataSetChanged()
    }
}
