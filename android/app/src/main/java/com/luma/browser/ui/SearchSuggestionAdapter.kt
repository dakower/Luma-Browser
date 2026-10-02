package com.luma.browser.ui

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.luma.browser.R

enum class SuggestionType {
    HISTORY,
    OPEN_TAB,
    SUGGESTION,
    SEARCH_ENGINE,
    BOOKMARK,
    INFO
}

data class SearchSuggestion(
    val title: String,
    val subtitle: String? = null,
    val targetUrl: String,
    val type: SuggestionType = SuggestionType.SUGGESTION,
    val iconRes: Int = R.drawable.ic_search
)

class SearchSuggestionAdapter(
    private val onItemClick: (SearchSuggestion) -> Unit,
    private val onFillClick: (SearchSuggestion) -> Unit
) : RecyclerView.Adapter<SearchSuggestionAdapter.VH>() {

    private val items = mutableListOf<SearchSuggestion>()

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val icon: ImageView = view.findViewById(R.id.suggIcon)
        val title: TextView = view.findViewById(R.id.suggTitle)
        val subtitle: TextView = view.findViewById(R.id.suggSubtitle)
        val actionIcon: ImageView = view.findViewById(R.id.suggActionIcon)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_search_suggestion, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val item = items[position]
        holder.title.text = item.title

        if (!item.subtitle.isNullOrBlank()) {
            holder.subtitle.visibility = View.VISIBLE
            holder.subtitle.text = item.subtitle
        } else {
            holder.subtitle.visibility = View.GONE
        }

        holder.icon.setImageResource(item.iconRes)

        when (item.type) {
            SuggestionType.HISTORY -> {
                holder.icon.setColorFilter(0xFF9E9AA8.toInt())
                holder.actionIcon.visibility = View.VISIBLE
            }
            SuggestionType.OPEN_TAB -> {
                holder.icon.setColorFilter(0xFF88E0AA.toInt())
                holder.actionIcon.visibility = View.VISIBLE
            }
            SuggestionType.SEARCH_ENGINE -> {
                holder.icon.setColorFilter(0xFFB490FF.toInt())
                holder.actionIcon.visibility = View.GONE
            }
            SuggestionType.BOOKMARK -> {
                holder.icon.setColorFilter(0xFFFFD166.toInt())
                holder.actionIcon.visibility = View.VISIBLE
            }
            SuggestionType.SUGGESTION -> {
                holder.icon.setColorFilter(0xFF716C82.toInt())
                holder.actionIcon.visibility = View.VISIBLE
            }
            SuggestionType.INFO -> {
                holder.icon.setColorFilter(0xFF716C82.toInt())
                holder.actionIcon.visibility = View.GONE
            }
        }

        holder.itemView.setOnClickListener {
            onItemClick(item)
        }

        holder.actionIcon.setOnClickListener {
            onFillClick(item)
        }
    }

    override fun getItemCount() = items.size

    fun submitList(newItems: List<SearchSuggestion>) {
        items.clear()
        items.addAll(newItems)
        notifyDataSetChanged()
    }
}
